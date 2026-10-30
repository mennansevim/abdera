using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Modules.Scheduling.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Tests.Integration;

// docs/10-decisions.md AI: "Pasife al" gelecekteki dersleri takvimden ve öğretmen programından
// kaldırır, geçmişe ve paraya dokunmaz, pasif öğrenciye yeni ders üretilmez.
public class StudentDeactivationFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public StudentDeactivationFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoginAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new Login.Request(email, password));
        response.EnsureSuccessStatusCode();
        return client;
    }

    private Task<HttpClient> AdminAsync() => LoginAsync("admin@test.local", "Test1234!");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Beklenmeyen durum: {(int)response.StatusCode}, gövde: {body}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(body, TestJson.Options)!;
    }

    private static Task<HttpResponseMessage> SetStatusAsync(
        HttpClient client, Guid studentId, StudentStatus status, EnrollmentEndReason? reason = null) =>
        client.PatchAsJsonAsync($"/api/students/{studentId}",
            new Students.UpdateRequest("Pasif", "Deneme", new DateOnly(2014, 1, 1), status, null, reason, reason is null ? null : "Şehir değişti"));

    private static async Task<List<Calendar.LessonResponse>> CalendarAsync(HttpClient admin, Guid teacherId)
    {
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(90).ToString("O"));
        return await ReadAsync<List<Calendar.LessonResponse>>(await admin.GetAsync($"/api/calendar?from={from}&to={to}&teacherId={teacherId}"));
    }

    [Fact]
    public async Task Deactivating_removes_future_lessons_and_keeps_history_and_money()
    {
        var admin = await AdminAsync();
        var seeded = await MessagingFlowTests.SeedLessonAsync(admin, "pasif" + Guid.NewGuid().ToString("N")[..8]);
        var receivable = await ReadAsync<Receivables.ReceivableResponse>(
            await admin.PostAsJsonAsync("/api/receivables", new Receivables.CreateRequest(seeded.EnrollmentId, "2099-05")));

        var now = DateTimeOffset.UtcNow;
        Guid pastLessonId, makeupLessonId, creditId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
            var seriesId = await db.LessonSeries.Where(s => s.EnrollmentId == seeded.EnrollmentId).Select(s => s.Id).SingleAsync();
            var pianoId = await db.Enrollments.Where(e => e.Id == seeded.EnrollmentId).Select(e => e.InstrumentId).SingleAsync();

            var past = Lesson.CreateFromSeries(seriesId, seeded.StudentId, seeded.TeacherId, pianoId, now.AddDays(-7), now.AddDays(-7).AddMinutes(45), now);
            past.Complete(now);
            var makeup = Lesson.CreateMakeup(seeded.StudentId, seeded.TeacherId, pianoId, now.AddDays(3).AddHours(5), now.AddDays(3).AddHours(6), now);
            var credit = MakeupCredit.Earn(seeded.StudentId, past.Id, MakeupCreditEarnedReason.SchoolCancelled, now, 60);
            credit.Use(makeup.Id, now);
            db.Lessons.AddRange(past, makeup);
            db.MakeupCredits.Add(credit);
            await db.SaveChangesAsync();
            (pastLessonId, makeupLessonId, creditId) = (past.Id, makeup.Id, credit.Id);
        }

        var futureCount = (await CalendarAsync(admin, seeded.TeacherId)).Count;
        var impact = await ReadAsync<StudentDeactivation.ImpactResponse>(await admin.GetAsync($"/api/students/{seeded.StudentId}/deactivation-impact"));
        Assert.Equal(futureCount, impact.FutureLessons);
        Assert.Equal(1, impact.ActiveEnrollments);
        Assert.Single(impact.Teachers);

        // Etki sorgusu (onay penceresinin vazgeçilen hâli) hiçbir şeyi değiştirmez.
        Assert.Equal(futureCount, (await CalendarAsync(admin, seeded.TeacherId)).Count);
        Assert.Equal(StudentStatus.Active,
            (await ReadAsync<Students.StudentResponse>(await admin.GetAsync($"/api/students/{seeded.StudentId}"))).Status);

        (await SetStatusAsync(admin, seeded.StudentId, StudentStatus.Inactive, EnrollmentEndReason.Moved)).EnsureSuccessStatusCode();

        Assert.Empty(await CalendarAsync(admin, seeded.TeacherId));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();

            var student = await db.Students.SingleAsync(s => s.Id == seeded.StudentId);
            Assert.Equal(StudentStatus.Inactive, student.Status);

            var enrollment = await db.Enrollments.SingleAsync(e => e.Id == seeded.EnrollmentId);
            Assert.Equal(EnrollmentStatus.Ended, enrollment.Status);
            Assert.Equal(EnrollmentEndReason.Moved, enrollment.EndReason);
            Assert.All(await db.LessonSeries.Where(s => s.EnrollmentId == seeded.EnrollmentId).ToListAsync(),
                series => Assert.Equal(LessonSeriesStatus.Ended, series.Status));

            // Geçmiş ders durur; telafi dersi kalktı, harcanan hak geri döndü, yeni hak doğmadı.
            var pastLesson = await db.Lessons.SingleAsync(l => l.Id == pastLessonId);
            Assert.Equal(LessonStatus.Completed, pastLesson.Status);
            Assert.False(await db.Lessons.AnyAsync(l => l.Id == makeupLessonId));
            var credit = await db.MakeupCredits.SingleAsync(c => c.Id == creditId);
            Assert.Equal(MakeupCreditStatus.Available, credit.Status);
            Assert.Null(credit.UsedLessonId);
            Assert.Equal(1, await db.MakeupCredits.CountAsync(c => c.StudentId == seeded.StudentId));

            var receivableAfter = await db.Receivables.SingleAsync(r => r.Id == receivable.Id);
            Assert.Equal(receivable.Amount, receivableAfter.Amount);
            Assert.Equal(receivable.Status, receivableAfter.Status);

            var job = await db.NotificationJobs.SingleAsync(j => j.ReferenceType == "lesson" && j.ReferenceId == seeded.LessonId);
            Assert.Equal(NotificationJobStatus.Cancelled, job.Status);

            var audit = await db.AuditLogs.SingleAsync(a => a.Action == "student.deactivated" && a.EntityId == seeded.StudentId);
            Assert.Contains(seeded.LessonId.ToString(), audit.AfterJson);
            Assert.Contains(seeded.TeacherId.ToString(), audit.AfterJson);
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "enrollment.ended" && a.EntityId == seeded.EnrollmentId));
        }

        // Pasif öğrenciye yeni program açılamaz, kapalı seri üretim yapmaz.
        var series2 = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            seeded.EnrollmentId, DayOfWeek.Friday, new TimeOnly(15, 0), 45, DateOnly.FromDateTime(DateTime.UtcNow), null));
        Assert.False(series2.IsSuccessStatusCode);
        Assert.Empty(await CalendarAsync(admin, seeded.TeacherId));
    }

    [Fact]
    public async Task Inactive_student_with_a_leftover_open_series_gets_no_new_lessons()
    {
        var admin = await AdminAsync();
        var seeded = await MessagingFlowTests.SeedLessonAsync(admin, "eski" + Guid.NewGuid().ToString("N")[..8]);

        // Bu kural gelmeden pasife alınmış öğrenci: durum Inactive ama seri açık, dersler silinmiş.
        Guid seriesId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
            var student = await db.Students.SingleAsync(s => s.Id == seeded.StudentId);
            student.SetStatus(StudentStatus.Inactive, DateTimeOffset.UtcNow);
            seriesId = await db.LessonSeries.Where(s => s.EnrollmentId == seeded.EnrollmentId).Select(s => s.Id).SingleAsync();
            db.Lessons.RemoveRange(await db.Lessons.Where(l => l.LessonSeriesId == seriesId).ToListAsync());
            await db.SaveChangesAsync();
        }

        var generation = await ReadAsync<LessonSeriesFeatures.GenerationSummary>(
            await admin.PostAsync($"/api/lesson-series/{seriesId}/generate", null));

        Assert.Equal(0, generation.Created);
        Assert.Empty(await CalendarAsync(admin, seeded.TeacherId));
    }

    [Fact]
    public async Task Deactivating_a_group_member_removes_only_that_student()
    {
        var admin = await AdminAsync();
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var art = instruments.Single(i => i.Code == "ART");
        var teacher = (await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync("/api/teachers",
            new Teachers.CreateRequest("Grup", $"Hoca{Guid.NewGuid():N}"[..12], [art.Id], null)))).Teacher;

        var firstDay = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        while (firstDay.DayOfWeek != DayOfWeek.Saturday) firstDay = firstDay.AddDays(1);
        var group = await ReadAsync<LessonGroups.LessonGroupResponse>(await admin.PostAsJsonAsync("/api/lesson-groups",
            new LessonGroups.CreateRequest("Resim", teacher.Id, art.Id, DayOfWeek.Saturday, new TimeOnly(10, 0), firstDay)));

        var leaving = await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync("/api/students",
            new Students.CreateRequest("Giden", "Uye", new DateOnly(2015, 3, 3))));
        var staying = await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync("/api/students",
            new Students.CreateRequest("Kalan", "Uye", new DateOnly(2015, 3, 3))));
        foreach (var student in new[] { leaving, staying })
        {
            (await admin.PostAsJsonAsync($"/api/lesson-groups/{group.Id}/members", new LessonGroups.AddMemberRequest(student.Id)))
                .EnsureSuccessStatusCode();
        }

        (await SetStatusAsync(admin, leaving.Id, StudentStatus.Inactive)).EnsureSuccessStatusCode();

        var calendar = await CalendarAsync(admin, teacher.Id);
        Assert.NotEmpty(calendar);
        Assert.All(calendar, lesson => Assert.Equal(staying.Id, lesson.StudentId));

        var groups = await ReadAsync<List<LessonGroups.LessonGroupResponse>>(await admin.GetAsync("/api/lesson-groups"));
        var after = groups.Single(g => g.Id == group.Id);
        Assert.Equal(LessonGroupStatus.Active, after.Status);
        Assert.Equal(staying.Id, Assert.Single(after.Members).StudentId);
    }

    [Fact]
    public async Task Teacher_deactivates_own_student_and_the_lessons_leave_their_schedule()
    {
        var admin = await AdminAsync();
        var email = $"kendi-{Guid.NewGuid():N}@test.local";
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var piano = instruments.Single(i => i.Code == "PIANO");
        var created = await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync("/api/teachers",
            new Teachers.CreateRequest("Tek", "Hocali", [piano.Id], email)));
        var student = await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync("/api/students",
            new Students.CreateRequest("Tek", "Ogrenci", new DateOnly(2015, 3, 3))));
        var enrollment = await ReadAsync<Enrollments.EnrollmentResponse>(await admin.PostAsJsonAsync(
            $"/api/students/{student.Id}/enrollments", new Enrollments.CreateRequest(created.Teacher.Id, piano.Id, new DateOnly(2026, 8, 1))));
        (await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, DayOfWeek.Thursday, new TimeOnly(16, 0), 45, DateOnly.FromDateTime(DateTime.UtcNow), null))).EnsureSuccessStatusCode();

        var teacher = await LoginAsync(email, created.TemporaryPassword!);
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(90).ToString("O"));
        Assert.NotEmpty(await ReadAsync<List<Calendar.LessonResponse>>(await teacher.GetAsync($"/api/calendar?from={from}&to={to}")));

        (await SetStatusAsync(teacher, student.Id, StudentStatus.Inactive)).EnsureSuccessStatusCode();

        Assert.Empty(await ReadAsync<List<Calendar.LessonResponse>>(await teacher.GetAsync($"/api/calendar?from={from}&to={to}")));
    }

    [Fact]
    public async Task Teacher_cannot_deactivate_a_student_who_also_studies_with_another_teacher()
    {
        var admin = await AdminAsync();
        var email = $"pasif-{Guid.NewGuid():N}@test.local";
        var seeded = await MessagingFlowTests.SeedLessonAsync(admin, "ikili" + Guid.NewGuid().ToString("N")[..8]);

        var mine = await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync("/api/teachers",
            new Teachers.CreateRequest("Kendi", "Hocasi", [seeded.SpareInstrumentIds[0]], email)));
        (await admin.PostAsJsonAsync($"/api/students/{seeded.StudentId}/enrollments",
            new Enrollments.CreateRequest(mine.Teacher.Id, seeded.SpareInstrumentIds[0], new DateOnly(2026, 8, 1)))).EnsureSuccessStatusCode();
        var teacher = await LoginAsync(email, mine.TemporaryPassword!);

        var impact = await ReadAsync<StudentDeactivation.ImpactResponse>(await teacher.GetAsync($"/api/students/{seeded.StudentId}/deactivation-impact"));
        Assert.True(impact.RequiresAdmin);
        Assert.Empty(impact.Teachers);

        var before = (await CalendarAsync(admin, seeded.TeacherId)).Count;
        var response = await SetStatusAsync(teacher, seeded.StudentId, StudentStatus.Inactive);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, (await CalendarAsync(admin, seeded.TeacherId)).Count);
        Assert.Equal(StudentStatus.Active,
            (await ReadAsync<Students.StudentResponse>(await admin.GetAsync($"/api/students/{seeded.StudentId}"))).Status);
    }
}
