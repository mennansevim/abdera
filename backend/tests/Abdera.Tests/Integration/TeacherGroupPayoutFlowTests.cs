using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Grup dersi (Resim) öğretmen ücreti: dönemin tahakkuk eden grup aidatlarının %40'ı, ayda bir
// (docs/10-decisions.md AC). Grup dersleri haftalık ders başı ödemeye girmez - çift ödeme bekçisi.
public class TeacherGroupPayoutFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public TeacherGroupPayoutFlowTests(AbderaWebApplicationFactory factory)
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
        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Beklenmeyen durum: {(int)response.StatusCode} {response.StatusCode}, gövde: {body}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(body, TestJson.Options)!;
    }

    private record Seeded(Guid TeacherId, string Period, TeacherPayWeek Week);

    // Geçmiş bir ayda: iki geçerli grup aidatı (biri indirimli), bir iptal edilmiş grup aidatı ve
    // aynı öğretmenin bir birebir aidatı. Aynı ayın geçmiş bir haftasında iki tamamlanmış grup dersi
    // ve bir tamamlanmış birebir ders.
    private async Task<Seeded> SeedAsync(string lastName)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Istanbul).Date);
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);
        var period = BillingPeriod.Format(month.Year, month.Month);
        var week = TeacherPayWeek.Containing(month.AddDays(10));

        await using var db = await _factory.CreateDbContextAsync();
        var art = await db.Instruments.SingleAsync(i => i.Code == "ART");
        var piano = await db.Instruments.SingleAsync(i => i.Code == "PIANO");
        var tariffId = await db.TuitionRates.Select(rate => rate.Id).FirstAsync();
        var teacher = Teacher.Create("Grup", lastName, now);
        db.Teachers.Add(teacher);

        var names = new Queue<string>(["Ayşe", "Burak", "Cem", "Derya"]);
        Enrollment Enroll(Instrument instrument, CourseKind kind)
        {
            var student = Student.Create(names.Dequeue(), lastName, new DateOnly(2015, 1, 1), now);
            db.Students.Add(student);
            var enrollment = Enrollment.Create(student.Id, teacher.Id, instrument.Id, kind, month, now);
            db.Enrollments.Add(enrollment);
            return enrollment;
        }

        Receivable Due(Enrollment enrollment, decimal baseAmount, decimal discount) =>
            Receivable.Create(enrollment.Id, tariffId, period,
                new TuitionCalculator.Breakdown(baseAmount, discount, discount > 0 ? "Kardeş" : null, baseAmount * (1 - discount / 100m)),
                "TRY", month.AddDays(9), now);

        var groupA = Enroll(art, CourseKind.Group);
        var groupB = Enroll(art, CourseKind.Group);
        var groupCancelled = Enroll(art, CourseKind.Group);
        var individual = Enroll(piano, CourseKind.Individual);
        db.Receivables.Add(Due(groupA, 4500m, 0m));
        db.Receivables.Add(Due(groupB, 4500m, 10m));
        var cancelled = Due(groupCancelled, 4500m, 0m);
        cancelled.Cancel(now);
        db.Receivables.Add(cancelled);
        db.Receivables.Add(Due(individual, 6000m, 0m));

        Lesson Completed(Enrollment enrollment, int hour)
        {
            var startAt = LessonGenerator.ToUtcInstant(week.Start.AddDays(5), new TimeOnly(hour, 0), Istanbul);
            var lesson = Lesson.CreateMakeup(enrollment.StudentId, teacher.Id, enrollment.InstrumentId, startAt, startAt.AddMinutes(90), now);
            lesson.Complete(now);
            return lesson;
        }
        db.Lessons.Add(Completed(groupA, 10));
        db.Lessons.Add(Completed(groupB, 10));
        db.Lessons.Add(Completed(individual, 13));

        await db.SaveChangesAsync();
        return new Seeded(teacher.Id, period, week);
    }

    [Fact]
    public void Share_is_forty_percent_rounded_to_kurus()
    {
        Assert.Equal(3420m, GroupLessonPay.ComputeAmount(8550m));
        Assert.Equal(0.4m, GroupLessonPay.ComputeAmount(1m));
        Assert.Equal(1333.33m, GroupLessonPay.ComputeAmount(3333.33m));
    }

    [Fact]
    public async Task Group_teacher_gets_forty_percent_of_accrued_group_dues_once_a_month_and_group_lessons_leave_the_weekly_count()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedAsync("Payli");

        var month = await ReadAsync<TeacherGroupPayouts.MonthResponse>(await admin.GetAsync($"/api/teacher-payouts/group-month?period={seeded.Period}"));
        var row = Assert.Single(month.Teachers, t => t.TeacherId == seeded.TeacherId);
        // 4.500 + 4.050 (iptal edilen ve birebir aidat sayılmaz) = 8.550; %40 = 3.420.
        Assert.Equal(8550m, row.RevenueBase);
        Assert.Equal(2, row.ReceivableCount);
        Assert.Equal(40m, row.SharePercent);
        Assert.Equal(3420m, row.ComputedAmount);
        Assert.Null(row.Payout);

        // Haftalık tablo yalnızca birebir dersi sayar.
        var week = await ReadAsync<TeacherPayouts.WeekResponse>(await admin.GetAsync($"/api/teacher-payouts/week?weekStart={seeded.Week.Start:yyyy-MM-dd}"));
        Assert.Equal(1, Assert.Single(week.Teachers, t => t.TeacherId == seeded.TeacherId).CompletedLessons);

        // Ekranda görülen tutar sunucununkiyle uyuşmazsa ödeme geçmez.
        var stale = await admin.PostAsJsonAsync("/api/teacher-payouts/group-month", new TeacherGroupPayouts.CreateRequest(
            seeded.TeacherId, seeded.Period, 9000m, 3600m, null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var paid = await ReadAsync<TeacherGroupPayouts.PayoutResponse>(await admin.PostAsJsonAsync("/api/teacher-payouts/group-month",
            new TeacherGroupPayouts.CreateRequest(seeded.TeacherId, seeded.Period, 8550m, 3420m, null, null, "Ekim payı")));
        Assert.Equal(3420m, paid.Amount);
        Assert.Equal(8550m, paid.RevenueBase);

        var twice = await admin.PostAsJsonAsync("/api/teacher-payouts/group-month", new TeacherGroupPayouts.CreateRequest(
            seeded.TeacherId, seeded.Period, 8550m, 3420m, null, null, null));
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);

        await using var db = await _factory.CreateDbContextAsync();
        var expense = await db.Expenses.SingleAsync(item => item.Id == paid.ExpenseId);
        Assert.Equal(ExpenseCategory.Salary, expense.Category);
        Assert.Equal(3420m, expense.Amount);
        Assert.True(await db.AuditLogs.AnyAsync(log => log.Action == "teacher_group_payout.created" && log.EntityId == paid.Id));

        var after = await ReadAsync<TeacherGroupPayouts.MonthResponse>(await admin.GetAsync($"/api/teacher-payouts/group-month?period={seeded.Period}"));
        Assert.Equal(3420m, Assert.Single(after.Teachers, t => t.TeacherId == seeded.TeacherId).Payout!.Amount);
    }

    [Fact]
    public async Task A_month_without_group_revenue_or_in_the_future_cannot_be_paid()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedAsync("Bos");
        var (year, month) = BillingPeriod.Parse(seeded.Period);
        var earlier = BillingPeriod.Format(new DateOnly(year, month, 1).AddMonths(-1).Year, new DateOnly(year, month, 1).AddMonths(-1).Month);
        var empty = await admin.PostAsJsonAsync("/api/teacher-payouts/group-month", new TeacherGroupPayouts.CreateRequest(
            seeded.TeacherId, earlier, 0m, 0m, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2);
        var notYet = await admin.PostAsJsonAsync("/api/teacher-payouts/group-month", new TeacherGroupPayouts.CreateRequest(
            seeded.TeacherId, BillingPeriod.Format(future.Year, future.Month), 1m, 0.4m, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, notYet.StatusCode);
    }
}
