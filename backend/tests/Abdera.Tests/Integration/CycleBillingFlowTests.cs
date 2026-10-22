using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Attendance.Features;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Tests.Integration;

// Dönem (4 ders) tabanlı aidat - docs/10-decisions.md AD. Birebir kayıtta Geldi/Mazeretli her
// yoklama kartın bir satırını doldurur; dönemin ilk dersi işlenince o dönemin aidatı tahakkuk
// eder. Tarife migration ile seed edilir (Birebir 6.000, 1 Eylül 2026'dan itibaren). Testler
// gerçek saatle koştuğu için dersler bugünden geriye kurulur ve geçiş tarihi onlardan öncedir.
public class CycleBillingFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public CycleBillingFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;
    private static DateOnly CycleStart => DateOnly.FromDateTime(Now.AddDays(-50).UtcDateTime);

    private async Task<HttpClient> AdminAsync()
    {
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();

        // Dönem aidatını bu sınıfın veritabanında aç (şablonda kapalı - AbderaWebApplicationFactory).
        var policy = await ReadAsync<BillingPolicy.PolicyResponse>(await client.GetAsync("/api/billing-policy"));
        if (policy.CycleBillingStartsOn != CycleStart)
        {
            (await client.PutAsJsonAsync("/api/billing-policy", new BillingPolicy.UpdateRequest(
                policy.MultiCourseDiscountPercent, policy.SiblingDiscountPercent, policy.DueDayOfMonth,
                policy.PrepayTiers.Select(tier => new BillingPolicy.TierRequest(tier.MinMonths, tier.Percent)).ToList(),
                CycleStart))).EnsureSuccessStatusCode();
        }
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Beklenmeyen durum: {(int)response.StatusCode} {response.StatusCode}, gövde: {body}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(body, TestJson.Options)!;
    }

    private record Seeded(Guid StudentId, Guid TeacherId, Guid InstrumentId, Guid EnrollmentId, List<Guid> LessonIds);

    // Kayıt + bugünden geriye, 3 gün arayla `count` geçmiş ders.
    private async Task<Seeded> SeedAsync(HttpClient admin, string name, int count)
    {
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var piano = instruments.Single(instrument => instrument.Code == "PIANO").Id;
        var teacher = (await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync(
            "/api/teachers", new Teachers.CreateRequest(name, "Ogretmen", [piano], null)))).Teacher;
        var student = await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync(
            "/api/students", new Students.CreateRequest(name, "Kart", new DateOnly(2014, 1, 1), false)));
        var enrollment = await ReadAsync<Enrollments.EnrollmentResponse>(await admin.PostAsJsonAsync(
            $"/api/students/{student.Id}/enrollments",
            new Enrollments.CreateRequest(teacher.Id, piano, CycleStart, CourseKind.Individual)));

        await using var db = await _factory.CreateDbContextAsync();
        var lessons = Enumerable.Range(0, count)
            .Select(index => Now.AddDays(-40 + index * 3))
            .Select(start => Lesson.CreateMakeup(student.Id, teacher.Id, piano, start, start.AddMinutes(45), Now))
            .ToList();
        db.Lessons.AddRange(lessons);
        await db.SaveChangesAsync();
        return new Seeded(student.Id, teacher.Id, piano, enrollment.Id, lessons.Select(lesson => lesson.Id).ToList());
    }

    private static async Task MarkAsync(HttpClient admin, Guid lessonId, AttendanceStatus status) =>
        (await admin.PostAsJsonAsync($"/api/lessons/{lessonId}/attendance", new MarkAttendance.MarkRequest(status, null)))
            .EnsureSuccessStatusCode();

    private async Task<List<Receivable>> CyclesAsync(Guid enrollmentId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return (await db.Receivables.AsNoTracking().Where(r => r.EnrollmentId == enrollmentId).ToListAsync())
            .Where(r => BillingCycle.IsKey(r.Period))
            .OrderBy(r => r.Period)
            .ToList();
    }

    private async Task<AttendanceCards.Card> CardAsync(HttpClient admin, Guid enrollmentId)
    {
        var response = await ReadAsync<AttendanceCards.CardsResponse>(await admin.GetAsync("/api/attendance/cards"));
        Assert.Equal(CycleStart, response.CycleBillingStartsOn);
        return response.Cards.Single(card => card.EnrollmentId == enrollmentId);
    }

    [Fact]
    public async Task Each_cycle_accrues_on_its_first_lesson_and_excused_fills_a_row_without_a_makeup_credit()
    {
        var admin = await AdminAsync();
        var seeded = await SeedAsync(admin, "Dongu", 6);

        // Kayıt açılınca bu ayın aylık aidatı açılmadı - birebir ders geçiş sonrası dönemle faturalanır.
        await using (var db = await _factory.CreateDbContextAsync())
            Assert.False(await db.Receivables.AnyAsync(r => r.EnrollmentId == seeded.EnrollmentId && !r.Period.StartsWith("D")));

        // 1. ders: 1. dönem tahakkuk eder, vadesi ders gününden 7 gün sonra.
        await MarkAsync(admin, seeded.LessonIds[0], AttendanceStatus.Present);
        var first = Assert.Single(await CyclesAsync(seeded.EnrollmentId));
        Assert.Equal("D001", first.Period);
        Assert.Equal(6000m, first.Amount);
        Assert.Equal(seeded.LessonIds[0], first.AccruedLessonId);
        Assert.Equal(first.AccruedOn!.Value.AddDays(BillingCycle.DueDays), first.DueDate);

        // Gelmedi satır doldurmaz; Mazeretli doldurur ve telafi hakkı doğurmaz.
        await MarkAsync(admin, seeded.LessonIds[1], AttendanceStatus.Absent);
        await MarkAsync(admin, seeded.LessonIds[2], AttendanceStatus.Excused);
        await MarkAsync(admin, seeded.LessonIds[3], AttendanceStatus.Present);
        await MarkAsync(admin, seeded.LessonIds[4], AttendanceStatus.Present);
        Assert.Single(await CyclesAsync(seeded.EnrollmentId));
        await using (var db = await _factory.CreateDbContextAsync())
            Assert.False(await db.MakeupCredits.AnyAsync(credit => credit.StudentId == seeded.StudentId));

        // 4/4 doldu ama 1. dönem ödenmedi: kart borcu öne alır.
        var card = await CardAsync(admin, seeded.EnrollmentId);
        Assert.Equal(1, card.CurrentCycle);
        Assert.Equal(4, card.FilledInCurrent);
        Assert.Equal(AttendanceCards.CardState.Unpaid, card.State);
        Assert.Single(card.Cycles[0].Misses);

        // 1. dönem ödenince durum "ödeme bekleniyor" olur.
        await PayAsync(admin, seeded.EnrollmentId, "D001", 6000m);
        card = await CardAsync(admin, seeded.EnrollmentId);
        Assert.Equal(AttendanceCards.CardState.AwaitingPayment, card.State);

        // Sonraki ders 2. dönemi açar.
        await MarkAsync(admin, seeded.LessonIds[5], AttendanceStatus.Present);
        var cycles = await CyclesAsync(seeded.EnrollmentId);
        Assert.Equal(["D001", "D002"], cycles.Select(r => r.Period));
        Assert.Equal(seeded.LessonIds[5], cycles[1].AccruedLessonId);
        await using (var db = await _factory.CreateDbContextAsync())
            Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "receivable.cycle_accrued" && log.EntityId == cycles[1].Id));
    }

    [Fact]
    public async Task Correcting_the_opening_lesson_cancels_the_unpaid_cycle_and_marking_it_again_reopens_the_same_row()
    {
        var admin = await AdminAsync();
        var seeded = await SeedAsync(admin, "Duzeltme", 5);
        foreach (var lessonId in seeded.LessonIds) await MarkAsync(admin, lessonId, AttendanceStatus.Present);
        var second = (await CyclesAsync(seeded.EnrollmentId)).Single(r => r.Period == "D002");

        // 5. ders aslında gelinmemiş: 2. dönem geri alınır, ödenmemiş aidat iptal olur (silinmez).
        await MarkAsync(admin, seeded.LessonIds[4], AttendanceStatus.Absent);
        var cancelled = (await CyclesAsync(seeded.EnrollmentId)).Single(r => r.Period == "D002");
        Assert.Equal(second.Id, cancelled.Id);
        Assert.Equal(ReceivableStatus.Cancelled, cancelled.Status);

        // Yeniden Geldi: aynı satır yeniden açılır, ikinci bir D002 oluşmaz.
        await MarkAsync(admin, seeded.LessonIds[4], AttendanceStatus.Present);
        var reopened = (await CyclesAsync(seeded.EnrollmentId)).Single(r => r.Period == "D002");
        Assert.Equal(second.Id, reopened.Id);
        Assert.Equal(ReceivableStatus.Unpaid, reopened.Status);
    }

    [Fact]
    public async Task A_prepaid_next_cycle_is_accrued_by_its_first_lesson_instead_of_opening_a_new_row()
    {
        var admin = await AdminAsync();
        var seeded = await SeedAsync(admin, "Pesin", 5);
        foreach (var lessonId in seeded.LessonIds.Take(4)) await MarkAsync(admin, lessonId, AttendanceStatus.Present);

        // 1. dönem ve henüz başlamamış 2. dönem aynı ödeme penceresinden birlikte alınır.
        var quote = await ReadAsync<Collections.Quote>(await admin.GetAsync(
            $"/api/enrollments/{seeded.EnrollmentId}/collection-preview?periods=D001&periods=D002&prepay=false&sibling=false&multiCourse=false"));
        Assert.Empty(quote.Blockers);
        await PayAsync(admin, seeded.EnrollmentId, ["D001", "D002"], quote.Total);

        var prepaid = (await CyclesAsync(seeded.EnrollmentId)).Single(r => r.Period == "D002");
        Assert.Null(prepaid.AccruedOn);
        Assert.Equal(ReceivableStatus.Paid, prepaid.Status);
        Assert.Equal(AttendanceCards.CardState.NextPrepaid, (await CardAsync(admin, seeded.EnrollmentId)).State);

        await MarkAsync(admin, seeded.LessonIds[4], AttendanceStatus.Present);
        var cycles = await CyclesAsync(seeded.EnrollmentId);
        Assert.Equal(2, cycles.Count);
        var accrued = cycles.Single(r => r.Period == "D002");
        Assert.Equal(prepaid.Id, accrued.Id);
        Assert.Equal(seeded.LessonIds[4], accrued.AccruedLessonId);
        Assert.Equal(ReceivableStatus.Paid, accrued.Status);
    }

    [Fact]
    public async Task Lessons_before_the_cycle_start_show_on_the_card_but_neither_count_nor_accrue()
    {
        var admin = await AdminAsync();
        var seeded = await SeedAsync(admin, "Gecis", 2);

        // Kayıt geçişten 10 gün önce başlamış, o arada işlenmiş bir ders var.
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var enrollment = await db.Enrollments.SingleAsync(e => e.Id == seeded.EnrollmentId);
            db.Entry(enrollment).Property(e => e.StartedAt).CurrentValue = CycleStart.AddDays(-10);
            var start = new DateTimeOffset(CycleStart.AddDays(-5).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
            var before = Lesson.CreateMakeup(seeded.StudentId, seeded.TeacherId, seeded.InstrumentId, start, start.AddMinutes(45), Now);
            db.Lessons.Add(before);
            await db.SaveChangesAsync();
            seeded.LessonIds.Insert(0, before.Id);
        }
        await MarkAsync(admin, seeded.LessonIds[0], AttendanceStatus.Present);
        Assert.Empty(await CyclesAsync(seeded.EnrollmentId));

        var card = await CardAsync(admin, seeded.EnrollmentId);
        Assert.Equal(seeded.LessonIds[0], Assert.Single(card.EarlierLessons).LessonId);
        Assert.Equal(AttendanceCards.CardState.NotStarted, card.State);

        await MarkAsync(admin, seeded.LessonIds[1], AttendanceStatus.Present);
        card = await CardAsync(admin, seeded.EnrollmentId);
        Assert.Equal(1, card.FilledInCurrent);
        Assert.Equal("D001", Assert.Single(await CyclesAsync(seeded.EnrollmentId)).Period);
    }

    private static async Task SetCycleStartAsync(HttpClient admin, Guid enrollmentId, DateOnly? on) =>
        (await admin.PutAsJsonAsync($"/api/attendance/cards/{enrollmentId}/cycle-start", new AttendanceCards.CycleStartRequest(on)))
            .EnsureSuccessStatusCode();

    private async Task<DateOnly> LessonDateAsync(Guid lessonId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        var lesson = await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == lessonId);
        var clock = _factory.Services.GetRequiredService<Abdera.Api.Shared.IClock>();
        return DateOnly.FromDateTime(clock.ToSchoolLocal(lesson.StartAt).Date);
    }

    [Fact]
    public async Task A_mid_month_first_entry_starts_the_count_and_moving_it_re_syncs_the_cycles()
    {
        var admin = await AdminAsync();
        var seeded = await SeedAsync(admin, "Ilkgiris", 5);
        foreach (var lessonId in seeded.LessonIds) await MarkAsync(admin, lessonId, AttendanceStatus.Present);
        Assert.Equal(["D001", "D002"], (await CyclesAsync(seeded.EnrollmentId)).Select(r => r.Period));

        // İlk giriş 2. ders: 1. ders sayılmaz, 4 ders = tek dönem; ödenmemiş D002 iptal olur.
        var firstEntry = await LessonDateAsync(seeded.LessonIds[1]);
        await SetCycleStartAsync(admin, seeded.EnrollmentId, firstEntry);

        var cycles = await CyclesAsync(seeded.EnrollmentId);
        Assert.Equal(seeded.LessonIds[1], cycles.Single(r => r.Period == "D001").AccruedLessonId);
        Assert.Equal(ReceivableStatus.Cancelled, cycles.Single(r => r.Period == "D002").Status);

        var card = await CardAsync(admin, seeded.EnrollmentId);
        Assert.True(card.HasOwnCycleStart);
        Assert.Equal(firstEntry, card.CountingFrom);
        Assert.Equal(4, card.FilledInCurrent);
        Assert.Single(card.EarlierLessons);

        await using var db = await _factory.CreateDbContextAsync();
        Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "enrollment.cycle_start_set" && log.EntityId == seeded.EnrollmentId));
    }

    [Fact]
    public async Task The_monthly_due_of_the_first_entry_month_counts_as_the_first_cycle()
    {
        var admin = await AdminAsync();
        var seeded = await SeedAsync(admin, "Ekimaidat", 5);
        var firstEntry = await LessonDateAsync(seeded.LessonIds[0]);
        var month = Abdera.Api.Modules.Billing.Domain.BillingPeriod.Format(firstEntry.Year, firstEntry.Month);

        // Geçiş öncesi açılmış aylık aidat (eski kayıt) - doğrudan yazılır, dokunulmadan kalmalı.
        Guid monthlyId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            var rate = await db.TuitionRates.AsNoTracking().FirstAsync(r => r.CourseKind == CourseKind.Individual);
            var monthly = Receivable.Create(seeded.EnrollmentId, rate.Id, month,
                new TuitionCalculator.Breakdown(6000m, 0m, null, 6000m), "TRY", firstEntry, Now);
            db.Receivables.Add(monthly);
            await db.SaveChangesAsync();
            monthlyId = monthly.Id;
        }

        await SetCycleStartAsync(admin, seeded.EnrollmentId, firstEntry);
        foreach (var lessonId in seeded.LessonIds) await MarkAsync(admin, lessonId, AttendanceStatus.Present);

        // 1. dönem aylık aidatla karşılandı: yalnızca 5. ders 2. dönemi açtı.
        var cycles = await CyclesAsync(seeded.EnrollmentId);
        Assert.Equal("D002", Assert.Single(cycles).Period);
        Assert.Equal(seeded.LessonIds[4], cycles[0].AccruedLessonId);

        var card = await CardAsync(admin, seeded.EnrollmentId);
        Assert.Equal(monthlyId, card.Cycles.Single(cycle => cycle.Number == 1).Receivable!.Id);
        Assert.Equal(12000m, card.UnpaidAmount);

        await using var check = await _factory.CreateDbContextAsync();
        var untouched = await check.Receivables.AsNoTracking().SingleAsync(r => r.Id == monthlyId);
        Assert.Equal(month, untouched.Period);
        Assert.Null(untouched.AccruedOn);
    }

    [Fact]
    public async Task Monthly_dues_are_not_opened_for_an_individual_enrollment_after_the_cycle_start()
    {
        var admin = await AdminAsync();
        var seeded = await SeedAsync(admin, "Aylik", 0);
        var period = TestPeriods.Current(_factory.Services);

        var manual = await admin.PostAsJsonAsync("/api/receivables", new Receivables.CreateRequest(seeded.EnrollmentId, period));
        Assert.Equal(HttpStatusCode.Conflict, manual.StatusCode);

        var quote = await ReadAsync<Collections.Quote>(await admin.GetAsync(
            $"/api/enrollments/{seeded.EnrollmentId}/collection-preview?periods={period}"));
        Assert.NotEmpty(quote.Blockers);

        await using var db = await _factory.CreateDbContextAsync();
        await Abdera.Api.Modules.Billing.Infrastructure.BillingDailyJob.OpenCurrentPeriodAsync(
            db, _factory.Services.GetRequiredService<Abdera.Api.Shared.IClock>());
        Assert.False(await db.Receivables.AnyAsync(r => r.EnrollmentId == seeded.EnrollmentId));
    }

    private static Task PayAsync(HttpClient admin, Guid enrollmentId, string period, decimal total) =>
        PayAsync(admin, enrollmentId, [period], total);

    private static async Task PayAsync(HttpClient admin, Guid enrollmentId, List<string> periods, decimal total)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"/api/enrollments/{enrollmentId}/collections")
        {
            Content = JsonContent.Create(new Collections.CreateRequest(
                periods, new Collections.DiscountChoice(false, false, false, null),
                DateOnly.FromDateTime(Now.UtcDateTime), PaymentMethod.Cash, null, ExpectedTotal: total)),
        };
        message.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var response = await admin.SendAsync(message);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }
}
