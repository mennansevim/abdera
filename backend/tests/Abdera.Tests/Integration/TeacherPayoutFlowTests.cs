using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Öğretmenlere haftalık ders ödemesi (docs/10-decisions.md O1). Kullanıcı kuralı: "her
// cumartesi tamamlanan derslerin ödemesini yapıyorum".
//
// Uç noktalar gerçekten HTTP üzerinden çağrılır (CLAUDE.md: yalnızca DB'ye yazılan satırı
// saymak yetmez) - haftalık tablo birden fazla tablodan derlendiği ve sıralama/çeviri
// hataları ancak çalışma zamanında ortaya çıktığı için.
public class TeacherPayoutFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public TeacherPayoutFlowTests(AbderaWebApplicationFactory factory)
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

    // Testin koştuğu güne göre GEÇMİŞTE kalan, kapanmış bir ödeme haftası seçer: başlamamış
    // hafta ödenemez, bu yüzden sabit bir tarih yazmak takvime bağımlı kırılgan bir test olurdu.
    private static TeacherPayWeek PastWeek(int weeksAgo)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Istanbul).Date);
        return TeacherPayWeek.Containing(today.AddDays(-7 * weeksAgo));
    }

    private static Lesson CompletedLesson(Guid teacherId, DateOnly date, int hour, DateTimeOffset now)
    {
        var startAt = LessonGenerator.ToUtcInstant(date, new TimeOnly(hour, 0), Istanbul);
        var lesson = Lesson.CreateMakeup(Guid.NewGuid(), teacherId, Guid.NewGuid(), startAt, startAt.AddMinutes(45), now);
        lesson.Complete(now);
        return lesson;
    }

    private static Lesson PlannedLesson(Guid teacherId, DateOnly date, int hour, DateTimeOffset now)
    {
        var startAt = LessonGenerator.ToUtcInstant(date, new TimeOnly(hour, 0), Istanbul);
        return Lesson.CreateMakeup(Guid.NewGuid(), teacherId, Guid.NewGuid(), startAt, startAt.AddMinutes(45), now);
    }

    private async Task<(Guid TeacherId, TeacherPayWeek Week)> SeedTeacherWithCompletedWeekAsync(int weeksAgo, string lastName)
    {
        var week = PastWeek(weeksAgo);
        var now = DateTimeOffset.UtcNow;

        await using var db = await _factory.CreateDbContextAsync();
        var teacher = Teacher.Create("Haftalık", lastName, now);
        db.Teachers.Add(teacher);

        // Haftanın içinde üç TAMAMLANMIŞ ders - ödenecek olanlar.
        db.Lessons.Add(CompletedLesson(teacher.Id, week.Start.AddDays(1), 10, now));
        db.Lessons.Add(CompletedLesson(teacher.Id, week.Start.AddDays(3), 11, now));
        // Cumartesi 21:00: haftanın SON saatleri de sayılmalı (sınır hatası bekçisi).
        db.Lessons.Add(CompletedLesson(teacher.Id, week.End, 21, now));
        // Aynı haftada yoklaması girilmemiş bir ders - sayılmamalı.
        db.Lessons.Add(PlannedLesson(teacher.Id, week.Start.AddDays(2), 15, now));
        // Haftanın bitimindeki pazar günü tamamlanmış ders - ödeme haftası Pzt-Cmt, sayılmamalı.
        db.Lessons.Add(CompletedLesson(teacher.Id, week.ExclusiveEnd, 10, now));

        await db.SaveChangesAsync();
        return (teacher.Id, week);
    }

    private static TeacherPayouts.TeacherWeekRow RowFor(TeacherPayouts.WeekResponse week, Guid teacherId) =>
        Assert.Single(week.Teachers.Where(row => row.TeacherId == teacherId));

    private async Task<TeacherPayouts.WeekResponse> GetWeekAsync(HttpClient admin, DateOnly weekStart)
    {
        var response = await admin.GetAsync($"/api/teacher-payouts/week?weekStart={weekStart:yyyy-MM-dd}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Beklenmeyen durum: {response.StatusCode}, gövde: {body}");
        return (await response.Content.ReadFromJsonAsync<TeacherPayouts.WeekResponse>(TestJson.Options))!;
    }

    [Fact]
    public async Task Weekly_payout_counts_only_completed_lessons_and_lands_in_the_expense_ledger()
    {
        var admin = await CreateAdminClientAsync();
        var (teacherId, week) = await SeedTeacherWithCompletedWeekAsync(4, "Odenen");

        var rateResponse = await admin.PutAsJsonAsync(
            $"/api/teacher-payouts/rates/{teacherId}", new TeacherPayouts.RateRequest(450m, "TRY"));
        Assert.Equal(HttpStatusCode.OK, rateResponse.StatusCode);

        var beforePayment = await GetWeekAsync(admin, week.Start);
        var row = RowFor(beforePayment, teacherId);
        Assert.Equal(week.Start, beforePayment.WeekStart);
        Assert.Equal(week.End, beforePayment.WeekEnd);
        Assert.Equal(3, row.CompletedLessons);
        Assert.Equal(450m, row.RatePerLesson);
        Assert.Equal(1350m, row.ComputedAmount);
        Assert.Null(row.Payout);

        var createResponse = await admin.PostAsJsonAsync("/api/teacher-payouts", new TeacherPayouts.CreateRequest(
            teacherId, week.Start, 3, 1350m, null, null, "haftalık ders ödemesi"));
        var createBody = await createResponse.Content.ReadAsStringAsync();
        Assert.True(createResponse.StatusCode == HttpStatusCode.Created,
            $"Beklenmeyen durum: {createResponse.StatusCode}, gövde: {createBody}");
        var created = (await createResponse.Content.ReadFromJsonAsync<TeacherPayouts.PayoutResponse>(TestJson.Options))!;
        Assert.Equal(1350m, created.Amount);
        // Ödeme günü haftanın kapandığı cumartesidir.
        Assert.Equal(week.End, created.PaidOn);

        await using var db = await _factory.CreateDbContextAsync();
        var storedPayout = await db.TeacherWeeklyPayouts.AsNoTracking().SingleAsync(payout => payout.Id == created.Id);
        Assert.Equal(3, storedPayout.LessonCount);
        Assert.Equal(450m, storedPayout.RatePerLesson);
        Assert.Equal(1350m, storedPayout.ComputedAmount);

        // Ödeme gider defterine Maaş kategorisiyle düşer - Giderler ekranındaki her toplam
        // onu buradan sayar, ayrı bir öğretmen gideri defteri yok.
        var expense = await db.Expenses.AsNoTracking().SingleAsync(item => item.Id == storedPayout.ExpenseId);
        Assert.Equal(ExpenseCategory.Salary, expense.Category);
        Assert.Equal(1350m, expense.Amount);
        Assert.Equal(week.End, expense.ExpenseDate);

        Assert.True(await db.AuditLogs.AnyAsync(log =>
            log.Action == "teacher_payout.created" && log.EntityId == created.Id));

        var afterPayment = await GetWeekAsync(admin, week.Start);
        Assert.Equal(1350m, RowFor(afterPayment, teacherId).Payout!.Amount);
    }

    [Fact]
    public async Task The_same_week_cannot_be_paid_twice()
    {
        var admin = await CreateAdminClientAsync();
        var (teacherId, week) = await SeedTeacherWithCompletedWeekAsync(5, "Mukerrer");
        await admin.PutAsJsonAsync($"/api/teacher-payouts/rates/{teacherId}", new TeacherPayouts.RateRequest(300m, "TRY"));

        var first = await admin.PostAsJsonAsync("/api/teacher-payouts", new TeacherPayouts.CreateRequest(
            teacherId, week.Start, 3, 900m, null, null, null));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Aynı haftanın ortasındaki bir gün gönderilse bile hafta pazartesiye normalize edilir,
        // yani ikinci ödeme yine aynı haftaya denk gelir ve reddedilir.
        var second = await admin.PostAsJsonAsync("/api/teacher-payouts", new TeacherPayouts.CreateRequest(
            teacherId, week.Start.AddDays(3), 3, 900m, null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        await using var db = await _factory.CreateDbContextAsync();
        Assert.Equal(1, await db.TeacherWeeklyPayouts.CountAsync(payout =>
            payout.TeacherId == teacherId && payout.WeekStart == week.Start));
    }

    // Tutarı sunucu hesaplar; istemci yalnızca gördüğünü teyit eder. Ekran açıkken bir derse
    // yoklama girildiyse ödeme sessizce başka bir tutarla geçmemeli.
    [Fact]
    public async Task A_stale_lesson_count_from_the_screen_stops_the_payment()
    {
        var admin = await CreateAdminClientAsync();
        var (teacherId, week) = await SeedTeacherWithCompletedWeekAsync(6, "Bayat");
        await admin.PutAsJsonAsync($"/api/teacher-payouts/rates/{teacherId}", new TeacherPayouts.RateRequest(300m, "TRY"));

        var response = await admin.PostAsJsonAsync("/api/teacher-payouts", new TeacherPayouts.CreateRequest(
            teacherId, week.Start, 2, 600m, null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var db = await _factory.CreateDbContextAsync();
        Assert.False(await db.TeacherWeeklyPayouts.AnyAsync(payout => payout.TeacherId == teacherId));
    }

    [Fact]
    public async Task A_teacher_without_a_lesson_rate_cannot_be_paid()
    {
        var admin = await CreateAdminClientAsync();
        var (teacherId, week) = await SeedTeacherWithCompletedWeekAsync(7, "Ucretsiz");

        var response = await admin.PostAsJsonAsync("/api/teacher-payouts", new TeacherPayouts.CreateRequest(
            teacherId, week.Start, 3, 900m, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Yönetici elden yuvarlayabilir (küsürat); hesap yine önce sunucuda yapılır ve iki tutar
    // da satıra donar, böylece "bu tutar nereden geldi" sorusu cevapsız kalmaz.
    [Fact]
    public async Task An_admin_can_round_the_amount_and_both_numbers_are_kept()
    {
        var admin = await CreateAdminClientAsync();
        var (teacherId, week) = await SeedTeacherWithCompletedWeekAsync(8, "Yuvarlak");
        await admin.PutAsJsonAsync($"/api/teacher-payouts/rates/{teacherId}", new TeacherPayouts.RateRequest(333.33m, "TRY"));

        var response = await admin.PostAsJsonAsync("/api/teacher-payouts", new TeacherPayouts.CreateRequest(
            teacherId, week.Start, 3, 999.99m, 1000m, null, null));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Beklenmeyen durum: {response.StatusCode}, gövde: {body}");
        var created = (await response.Content.ReadFromJsonAsync<TeacherPayouts.PayoutResponse>(TestJson.Options))!;

        Assert.Equal(1000m, created.Amount);
        Assert.Equal(999.99m, created.ComputedAmount);

        await using var db = await _factory.CreateDbContextAsync();
        var expense = await db.Expenses.AsNoTracking().SingleAsync(item => item.Id == created.ExpenseId);
        Assert.Equal(1000m, expense.Amount);
    }

    // Maaş verisi Admin'e özeldir (docs/04-permissions.md) - oturumsuz istek uca hiç ulaşamaz.
    // Issue #16: öğretmen kendi haftasını görür - tamamlanan/eksik yoklama sayısı, ödeme durumu
    // ve haftanın dersleri yoklama durumlarıyla. Tutar dönmez (kullanıcı kararı); yoklama
    // düzeltmesi aynı listeye yansır.
    [Fact]
    public async Task A_teacher_sees_own_week_with_lesson_statuses_and_can_correct_attendance_from_it()
    {
        var admin = await CreateAdminClientAsync();
        var week = PastWeek(1);
        var now = DateTimeOffset.UtcNow;
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Abdera.Api.Modules.People.Features.Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");
        var teacherEmail = "my-week@test.local";
        var teacherCreate = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Abdera.Api.Modules.People.Features.Teachers.CreateRequest("Kendi", "Haftası", [piano.Id], teacherEmail)))
            .Content.ReadFromJsonAsync<Abdera.Api.Modules.People.Features.Teachers.CreateResponse>(TestJson.Options))!;
        var teacherId = teacherCreate.Teacher.Id;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Abdera.Api.Modules.People.Features.Students.CreateRequest("Hafta", "Öğrencisi", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Abdera.Api.Modules.People.Features.Students.StudentResponse>(TestJson.Options))!;

        Lesson Planned(DateOnly date, int hour)
        {
            var startAt = LessonGenerator.ToUtcInstant(date, new TimeOnly(hour, 0), Istanbul);
            return Lesson.CreateMakeup(student.Id, teacherId, piano.Id, startAt, startAt.AddMinutes(45), now);
        }
        var marked = Planned(week.Start, 16);
        var missing = Planned(week.Start.AddDays(2), 17);
        var sunday = Planned(week.ExclusiveEnd, 10);
        var cancelled = Planned(week.Start.AddDays(3), 18);
        cancelled.Cancel(now);
        await using (var db = await _factory.CreateDbContextAsync())
        {
            db.Lessons.AddRange(marked, missing, sunday, cancelled);
            await db.SaveChangesAsync();
        }

        using var teacher = _factory.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new Login.Request(teacherEmail, teacherCreate.TemporaryPassword!))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, (await teacher.PostAsJsonAsync($"/api/lessons/{marked.Id}/attendance",
            new Abdera.Api.Modules.Attendance.Features.MarkAttendance.MarkRequest(Abdera.Api.Modules.Attendance.Domain.AttendanceStatus.Present, null))).StatusCode);

        var url = $"/api/teacher-payouts/my-week?weekStart={week.Start:yyyy-MM-dd}";
        var myWeek = (await teacher.GetFromJsonAsync<TeacherPayouts.MyWeekResponse>(url, TestJson.Options))!;
        Assert.Equal(week.Start, myWeek.WeekStart);
        Assert.Equal(week.End, myWeek.PayDay);
        Assert.Equal(1, myWeek.CompletedLessons);
        Assert.Equal(1, myWeek.MissingAttendanceLessons);
        Assert.False(myWeek.IsPaid);
        // Pazar ve iptal edilen ders listede yok; sıralama başlangıç saatine göre.
        Assert.Equal([marked.Id, missing.Id], myWeek.Lessons.Select(l => l.LessonId));
        Assert.Equal(Abdera.Api.Modules.Attendance.Domain.AttendanceStatus.Present, myWeek.Lessons[0].AttendanceStatus);
        Assert.Null(myWeek.Lessons[1].AttendanceStatus);
        Assert.Equal("Hafta Öğrencisi", myWeek.Lessons[0].StudentName);

        // Haftalık listeden düzeltme: Geldi -> Mazeretli.
        Assert.Equal(HttpStatusCode.OK, (await teacher.PostAsJsonAsync($"/api/lessons/{marked.Id}/attendance",
            new Abdera.Api.Modules.Attendance.Features.MarkAttendance.MarkRequest(Abdera.Api.Modules.Attendance.Domain.AttendanceStatus.Excused, null))).StatusCode);
        var corrected = (await teacher.GetFromJsonAsync<TeacherPayouts.MyWeekResponse>(url, TestJson.Options))!;
        Assert.Equal(Abdera.Api.Modules.Attendance.Domain.AttendanceStatus.Excused, corrected.Lessons[0].AttendanceStatus);

        // Ücret bilgisi yanıtta yok; yönetici hesabı bu ucu kullanamaz.
        var raw = await teacher.GetStringAsync(url);
        Assert.DoesNotContain("amount", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task The_payout_week_is_closed_to_callers_without_an_admin_session()
    {
        var client = _factory.CreateClient();
        var week = PastWeek(4);

        var response = await client.GetAsync($"/api/teacher-payouts/week?weekStart={week.Start:yyyy-MM-dd}");

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Beklenmeyen durum: {response.StatusCode}");
    }
}
