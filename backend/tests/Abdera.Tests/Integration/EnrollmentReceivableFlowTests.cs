using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.Billing.Infrastructure;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Tests.Integration;

// İş kuralı: "Bir öğrenci kayıtlıysa her ay ödeme yapabiliyor olmalı." Kurs kaydı açıldığı
// anda o ayın aidatı açılır (EnrollmentReceivableOpener) - 24 saatlik MonthlyReceivableGenerator
// döngüsü beklenmez.
//
// Ayrı bir test sınıfı (dolayısıyla ayrı bir Postgres container'ı), çünkü tarife-yok testi Grup
// tarifesini bu veritabanında bilerek bozuyor; TuitionAndDuesFlowTests'in seed tarifelerine
// dayanan testlerini etkilememeli.
public class EnrollmentReceivableFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public EnrollmentReceivableFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Beklenmeyen durum: {(int)response.StatusCode} {response.StatusCode}, gövde: {body}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(body, TestJson.Options)!;
    }

    private async Task<Guid> InstrumentIdAsync(HttpClient admin, string code)
    {
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        return instruments.Single(instrument => instrument.Code == code).Id;
    }

    private async Task<Teachers.TeacherResponse> CreateTeacherAsync(HttpClient admin, string name, params Guid[] instrumentIds) =>
        (await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync(
            "/api/teachers", new Teachers.CreateRequest(name, "Ogretmen", instrumentIds, null)))).Teacher;

    private async Task<Students.StudentResponse> CreateStudentAsync(HttpClient admin, string name) =>
        await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync(
            "/api/students", new Students.CreateRequest(name, "Ogrenci", new DateOnly(2014, 1, 1))));

    private static Task<HttpResponseMessage> EnrollAsync(
        HttpClient admin, Guid studentId, Guid teacherId, Guid instrumentId, DateOnly startedAt,
        CourseKind kind = CourseKind.Individual) =>
        admin.PostAsJsonAsync(
            $"/api/students/{studentId}/enrollments",
            new Enrollments.CreateRequest(teacherId, instrumentId, startedAt, kind));

    // Ayın başından önce başlamış bir kayıt - "bu ay" hangi ay olursa olsun kapsanır.
    private static readonly DateOnly StartedEarlier = new(2026, 9, 1);

    [Fact]
    public async Task Creating_an_enrollment_opens_this_months_receivable_with_the_full_price_snapshot()
    {
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var teacher = await CreateTeacherAsync(admin, "Anlik", piano);
        var student = await CreateStudentAsync(admin, "Anlik");
        var period = TestPeriods.Current(_factory.Services);

        var enrollment = await ReadAsync<Enrollments.EnrollmentResponse>(
            await EnrollAsync(admin, student.Id, teacher.Id, piano, StartedEarlier));

        await using var db = await _factory.CreateDbContextAsync();
        var receivable = await db.Receivables.AsNoTracking()
            .SingleAsync(r => r.EnrollmentId == enrollment.Id);
        var rate = (await db.TuitionRates.AsNoTracking()
                .Where(r => r.CourseKind == CourseKind.Individual).ToListAsync())
            .Single(r => r.IsActiveOn(BillingPeriod.FirstDay(period)));
        var settings = await BillingSettings.GetCurrentAsync(db);

        // Fiyat snapshot'ı: hesabın tamamı satırda donmuş olmalı (CLAUDE.md).
        Assert.Equal(period, receivable.Period);
        Assert.Equal(rate.Id, receivable.TuitionRateId);
        Assert.Equal(rate.MonthlyAmount, receivable.BaseAmount);
        Assert.Equal(0m, receivable.DiscountPercent);
        Assert.Null(receivable.DiscountReason);
        Assert.Equal(rate.MonthlyAmount, receivable.Amount);
        Assert.Equal(rate.Currency, receivable.Currency);
        Assert.Equal(BillingPeriod.DueDate(period, settings.DueDayOfMonth), receivable.DueDate);
        Assert.Equal(ReceivableStatus.Unpaid, receivable.Status);

        var audit = await db.AuditLogs.AsNoTracking()
            .SingleAsync(log => log.Action == "receivable.enrollment_opened" && log.EntityId == receivable.Id);
        Assert.NotNull(audit.ActorUserId);

        // Admin ekranında hemen görünür ve tahsil edilebilir.
        var billing = await ReadAsync<List<StudentBilling.StudentBillingResponse>>(
            await admin.GetAsync($"/api/students/{student.Id}/billing"));
        Assert.Contains(billing.Single().Receivables, r => r.Period == period && r.Id == receivable.Id);
    }

    [Fact]
    public async Task Second_course_is_priced_with_the_multi_course_discount_because_the_enrollment_is_saved_first()
    {
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var guitar = await InstrumentIdAsync(admin, "GUITAR");
        var teacher = await CreateTeacherAsync(admin, "Ikinci", piano, guitar);
        var student = await CreateStudentAsync(admin, "Ikinci");

        await ReadAsync<Enrollments.EnrollmentResponse>(await EnrollAsync(admin, student.Id, teacher.Id, piano, StartedEarlier));
        var second = await ReadAsync<Enrollments.EnrollmentResponse>(
            await EnrollAsync(admin, student.Id, teacher.Id, guitar, StartedEarlier));

        await using var db = await _factory.CreateDbContextAsync();
        var receivable = await db.Receivables.AsNoTracking().SingleAsync(r => r.EnrollmentId == second.Id);
        Assert.Equal(5m, receivable.DiscountPercent);
        Assert.Equal("2 kurs indirimi (%5)", receivable.DiscountReason);
    }

    [Fact]
    public async Task Teacher_adding_a_new_student_also_opens_this_months_receivable()
    {
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var teacher = await CreateTeacherAsync(admin, "Yeniogrenci", piano);
        var period = TestPeriods.Current(_factory.Services);

        var created = await ReadAsync<Teachers.TeacherStudentResponse>(await admin.PostAsJsonAsync(
            $"/api/teachers/{teacher.Id}/students",
            new Teachers.CreateStudentRequest("Yeni", "Ogrenci", new DateOnly(2015, 1, 1), piano, StartedEarlier)));

        await using var db = await _factory.CreateDbContextAsync();
        Assert.True(await db.Receivables.AnyAsync(r => r.EnrollmentId == created.EnrollmentId && r.Period == period));
    }

    [Fact]
    public async Task Enrollment_starting_in_a_later_month_does_not_bill_this_month()
    {
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var teacher = await CreateTeacherAsync(admin, "Ileri", piano);
        var student = await CreateStudentAsync(admin, "Ileri");
        var nextMonth = BillingPeriod.FirstDay(TestPeriods.Current(_factory.Services)).AddMonths(1);

        var enrollment = await ReadAsync<Enrollments.EnrollmentResponse>(
            await EnrollAsync(admin, student.Id, teacher.Id, piano, nextMonth));

        await using var db = await _factory.CreateDbContextAsync();
        Assert.False(await db.Receivables.AnyAsync(r => r.EnrollmentId == enrollment.Id));
    }

    [Fact]
    public async Task Without_a_valid_tariff_the_enrollment_is_still_created_and_the_manual_open_explains_why()
    {
        var admin = await CreateAdminClientAsync();
        var art = await InstrumentIdAsync(admin, "ART");
        var teacher = await CreateTeacherAsync(admin, "Tarifesiz", art);
        var student = await CreateStudentAsync(admin, "Tarifesiz");
        var period = TestPeriods.Current(_factory.Services);
        var nextMonth = BillingPeriod.FirstDay(period).AddMonths(1);

        // Grup tarifesini bir sonraki aya kaydır: bu dönemin ilk gününde geçerli tarife kalmaz
        // (TuitionPricer.RateFor dönemin ilk gününe bakar).
        await using var db = await _factory.CreateDbContextAsync();
        var groupRates = await db.TuitionRates.AsNoTracking()
            .Where(r => r.CourseKind == CourseKind.Group)
            .Select(r => new { r.Id, r.EffectiveFrom })
            .ToListAsync();
        await db.TuitionRates.Where(r => r.CourseKind == CourseKind.Group && r.EffectiveUntil == null)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.EffectiveFrom, nextMonth));
        try
        {
            var response = await EnrollAsync(admin, student.Id, teacher.Id, art, StartedEarlier, CourseKind.Group);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var enrollment = (await response.Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

            // Sessizce 0 TL'lik aidat üretilmez - hiç satır yok.
            Assert.False(await db.Receivables.AnyAsync(r => r.EnrollmentId == enrollment.Id));

            // Aylık aidatlar ekranındaki "Bu ayın aidatını aç" düğmesinin çağırdığı uç: sebebi
            // ders türü ve dönemle birlikte söyler.
            var manual = await admin.PostAsJsonAsync(
                "/api/receivables", new Receivables.CreateRequest(enrollment.Id, period));
            Assert.Equal(HttpStatusCode.Conflict, manual.StatusCode);
            var body = await manual.Content.ReadAsStringAsync();
            Assert.Contains($"{period}: Grup ders", body);
            Assert.Contains("Fiyat politikası", body);
        }
        finally
        {
            foreach (var rate in groupRates)
            {
                await db.TuitionRates.Where(r => r.Id == rate.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(r => r.EffectiveFrom, rate.EffectiveFrom));
            }
        }
    }

    [Fact]
    public async Task Monthly_generator_and_manual_open_do_not_duplicate_the_receivable_opened_at_enrollment()
    {
        var admin = await CreateAdminClientAsync();
        var piano = await InstrumentIdAsync(admin, "PIANO");
        var teacher = await CreateTeacherAsync(admin, "Tekrar", piano);
        var student = await CreateStudentAsync(admin, "Tekrar");
        var period = TestPeriods.Current(_factory.Services);

        var enrollment = await ReadAsync<Enrollments.EnrollmentResponse>(
            await EnrollAsync(admin, student.Id, teacher.Id, piano, StartedEarlier));

        await using var db = await _factory.CreateDbContextAsync();
        var clock = _factory.Services.GetRequiredService<IClock>();

        // MonthlyReceivableGenerator'ın her tikte çağırdığı akış.
        var run = await MonthlyDueRun.RunAsync(db, clock, period, actorId: null, throwIfEmpty: false);
        Assert.DoesNotContain(run.Missing, row => row.EnrollmentId == enrollment.Id);

        // Servisi tekrar çağırmak da, elle açma ucu da ikinci satır üretmez.
        Assert.Equal(
            EnrollmentReceivableOpener.Outcome.AlreadyExists,
            await EnrollmentReceivableOpener.OpenCurrentPeriodAsync(db, clock, enrollment.Id, actorId: null));
        var manual = await admin.PostAsJsonAsync(
            "/api/receivables", new Receivables.CreateRequest(enrollment.Id, period));
        Assert.Equal(HttpStatusCode.Conflict, manual.StatusCode);

        Assert.Equal(1, await db.Receivables.CountAsync(r => r.EnrollmentId == enrollment.Id && r.Period == period));
    }
}
