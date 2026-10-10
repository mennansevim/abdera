using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Scheduling.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Issue #14 - ayrılma riski erken uyarısı ve kurs kaydı bitirilirken ayrılma nedeni.
// Uç çok tablolu bir sorgu (CLAUDE.md "Çok tablolu sorgularda OrderBy sırası"): EF çeviri
// hataları yalnızca sorgu çalışınca ortaya çıktığı için gerçekten HTTP üzerinden çağrılır.
public class ChurnSignalFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public ChurnSignalFlowTests(AbderaWebApplicationFactory factory)
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

    private record Seeded(Guid StudentId, Guid EnrollmentId, Guid TeacherId, Guid GuardianId, string TeacherEmail, string TeacherPassword, List<Guid> LessonIds);

    private async Task<Seeded> SeedStudentAsync(HttpClient admin, string suffix)
    {
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        var teacherEmail = $"churn-{suffix}@test.local";
        var teacherCreate = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest($"Ayrılma{suffix}", "Öğretmen", [piano.Id], teacherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest($"Ayrılma{suffix}", "Öğrenci", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var phoneDigits = (Math.Abs(suffix.GetHashCode()) % 10_000_000).ToString("D7");
        var guardian = (await (await admin.PostAsJsonAsync("/api/guardians",
                new Guardians.CreateRequest($"Ayrılma{suffix}", "Veli", $"0544{phoneDigits}")))
            .Content.ReadFromJsonAsync<Guardians.GuardianResponse>(TestJson.Options))!;
        await admin.PostAsJsonAsync($"/api/students/{student.Id}/guardians",
            new LinkGuardianToStudent.Request(guardian.Id, "anne", true));
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacherCreate.Teacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var seriesResponse = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, DayOfWeek.Thursday, new TimeOnly(15, 0), 45, DateOnly.FromDateTime(DateTime.UtcNow), null));
        Assert.Equal(HttpStatusCode.Created, seriesResponse.StatusCode);

        await using var db = await _factory.CreateDbContextAsync();
        var lessonIds = await db.Lessons.Where(l => l.StudentId == student.Id)
            .OrderBy(l => l.StartAt).Select(l => l.Id).Take(3).ToListAsync();
        return new Seeded(student.Id, enrollment.Id, teacherCreate.Teacher.Id, guardian.Id,
            teacherEmail, teacherCreate.TemporaryPassword!, lessonIds);
    }

    [Fact]
    public async Task Attention_list_explains_rsvp_practice_skill_and_overdue_signals_and_hides_money_from_teachers()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedStudentAsync(admin, "sig");
        var quiet = await SeedStudentAsync(admin, "quiet");
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using (var db = await _factory.CreateDbContextAsync())
        {
            // İki ders geçmişe alınıp ikisinde de veli "gelemiyorum" demiş.
            for (var i = 0; i < 2; i++)
            {
                var start = now.AddDays(-3 - 7 * i);
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE lessons SET start_at = {start}, end_at = {start.AddMinutes(45)} WHERE id = {seeded.LessonIds[i]}");
                var rsvp = LessonRsvp.Create(seeded.LessonIds[i], seeded.GuardianId, now);
                rsvp.Respond(RsvpResponse.NotAttending, RsvpSource.Admin, now);
                db.LessonRsvps.Add(rsvp);
            }

            // Pratik günlüğü 4-6 hafta önce düzenliydi, son 3 hafta boş.
            foreach (var daysAgo in new[] { 30, 37, 44 })
                db.PracticeJournalEntries.Add(PracticeJournalEntry.Create(seeded.StudentId, today.AddDays(-daysAgo), 20, "Gam", null, Guid.NewGuid(), now));

            // Ritim puanı 4'ten 2'ye düştü.
            var rhythmId = await db.SkillDefinitions.Where(s => s.Code == "RHYTHM").Select(s => s.Id).SingleAsync();
            db.SkillAssessments.Add(SkillAssessment.Create(seeded.StudentId, rhythmId, seeded.TeacherId, null, 4, null, now.AddDays(-40)));
            db.SkillAssessments.Add(SkillAssessment.Create(seeded.StudentId, rhythmId, seeded.TeacherId, null, 2, null, now.AddDays(-5)));

            // Üç aylık gecikmiş aidat (eşik 3 ay).
            var rate = await db.TuitionRates.AsNoTracking().FirstAsync(r => r.CourseKind == CourseKind.Individual);
            foreach (var period in new[] { "2026-04", "2026-05", "2026-06" })
            {
                var receivable = Receivable.Create(seeded.EnrollmentId, rate.Id, period,
                    new TuitionCalculator.Breakdown(1000m, 0m, null, 1000m), "TRY",
                    new DateOnly(int.Parse(period[..4]), int.Parse(period[5..]), 5), now);
                db.Receivables.Add(receivable);
            }
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE receivables SET status = 'Overdue' WHERE enrollment_id = {seeded.EnrollmentId} AND period IN ('2026-04', '2026-05', '2026-06')");

            // Günlüğü hiç tutmayan öğrenci için "boş günlük" sinyali üretilmez.
        }

        var response = await admin.GetAsync("/api/students/attention-needed");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<List<AttentionNeededStudents.Response>>(TestJson.Options))!;
        var row = list.Single(r => r.StudentId == seeded.StudentId);
        Assert.Contains(row.Reasons, r => r.Contains("\"gelemiyorum\"") && r.Contains("2 kez"));
        Assert.Contains("3 haftadır pratik günlüğü girilmedi (önceden düzenliydi)", row.Reasons);
        Assert.Contains("Yetenek puanı düştü: Ritim 4 → 2", row.Reasons);
        Assert.Contains("3 aylık aidat gecikmiş", row.Reasons);
        Assert.DoesNotContain(list, r => r.StudentId == quiet.StudentId);
        // En çok sinyali olan öğrenci listenin başında.
        Assert.Equal(row.Reasons.Count, list.Max(r => r.Reasons.Count));

        using var teacherClient = _factory.CreateClient();
        (await teacherClient.PostAsJsonAsync("/api/auth/login", new Login.Request(seeded.TeacherEmail, seeded.TeacherPassword))).EnsureSuccessStatusCode();
        var teacherList = (await teacherClient.GetFromJsonAsync<List<AttentionNeededStudents.Response>>(
            "/api/students/attention-needed", TestJson.Options))!;
        var teacherRow = Assert.Single(teacherList);
        Assert.Equal(seeded.StudentId, teacherRow.StudentId);
        Assert.DoesNotContain(teacherRow.Reasons, r => r.Contains("aidat"));
        Assert.Equal(3, teacherRow.Reasons.Count);
    }

    [Fact]
    public async Task Ending_an_enrollment_records_the_reason_and_note_in_the_row_and_the_audit_log()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedStudentAsync(admin, "end");

        var response = await admin.PostAsJsonAsync($"/api/students/{seeded.StudentId}/enrollments/{seeded.EnrollmentId}/end",
            new Enrollments.EndRequest(EnrollmentEndReason.Moved, "  Ankara'ya taşındılar  "));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var enrollment = await db.Enrollments.AsNoTracking().SingleAsync(e => e.Id == seeded.EnrollmentId);
        Assert.Equal(EnrollmentStatus.Ended, enrollment.Status);
        Assert.Equal(EnrollmentEndReason.Moved, enrollment.EndReason);
        Assert.Equal("Ankara'ya taşındılar", enrollment.EndNote);
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Action == "enrollment.ended" && a.EntityId == seeded.EnrollmentId);
        using (var after = System.Text.Json.JsonDocument.Parse(audit.AfterJson!))
            Assert.Equal("Moved", after.RootElement.GetProperty("EndReason").GetString());

        var listed = (await admin.GetFromJsonAsync<List<Enrollments.EnrollmentResponse>>(
            $"/api/students/{seeded.StudentId}/enrollments", TestJson.Options))!;
        Assert.Equal(EnrollmentEndReason.Moved, listed.Single(e => e.Id == seeded.EnrollmentId).EndReason);

        // Sonlanmış kayıt sinyal listesinde yer almaz.
        var attention = (await admin.GetFromJsonAsync<List<AttentionNeededStudents.Response>>(
            "/api/students/attention-needed", TestJson.Options))!;
        Assert.DoesNotContain(attention, r => r.StudentId == seeded.StudentId);
    }

    [Fact]
    public async Task Ending_an_enrollment_rejects_an_unknown_reason_and_an_overlong_note()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedStudentAsync(admin, "end-bad");
        var url = $"/api/students/{seeded.StudentId}/enrollments/{seeded.EnrollmentId}/end";

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync(url, new { reason = 99, note = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync(url, new Enrollments.EndRequest(EnrollmentEndReason.Other, new string('x', 501)))).StatusCode);

        Assert.Equal(EnrollmentStatus.Active,
            (await db.Enrollments.AsNoTracking().SingleAsync(e => e.Id == seeded.EnrollmentId)).Status);
    }
}
