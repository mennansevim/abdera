using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Progress.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Issue #15 - dönem karnesi. Uçlar çok tablolu sorgular içerdiği için gerçekten HTTP üzerinden
// çağrılır (CLAUDE.md). Kurallar: öğretmen yalnızca kendi kaydının karnesini yazar, veli yalnızca
// yayınlanmış karneyi görür, yayınlanan karnenin verisi donar.
public class TermReportFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    public TermReportFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();
        return client;
    }

    private async Task<HttpClient> LoginTeacherAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new Login.Request(email, password))).EnsureSuccessStatusCode();
        return client;
    }

    private async Task<HttpClient> LoginGuardianAsync(string phone)
    {
        var client = _factory.CreateClient();
        var otp = (await (await client.PostAsJsonAsync("/api/guardian/otp/request", new GuardianAuth.RequestOtpRequest(phone)))
            .Content.ReadFromJsonAsync<GuardianAuth.RequestOtpResponse>(TestJson.Options))!;
        (await client.PostAsJsonAsync("/api/guardian/otp/verify", new GuardianAuth.VerifyOtpRequest(phone, otp.DebugCode!))).EnsureSuccessStatusCode();
        return client;
    }

    private record Seeded(Guid StudentId, Guid EnrollmentId, Guid TeacherId, Guid InstrumentId, string TeacherEmail, string TeacherPassword, string GuardianPhone, ReportTerm Term);

    private async Task<Seeded> SeedAsync(HttpClient admin, string suffix)
    {
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");
        var teacherEmail = $"karne-{suffix}@test.local";
        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest($"Karne{suffix}", "Öğretmen", [piano.Id], teacherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest($"Karne{suffix}", "Öğrenci", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var phone = $"0533{(Math.Abs(suffix.GetHashCode()) % 10_000_000):D7}";
        var guardian = (await (await admin.PostAsJsonAsync("/api/guardians", new Guardians.CreateRequest($"Karne{suffix}", "Veli", phone)))
            .Content.ReadFromJsonAsync<Guardians.GuardianResponse>(TestJson.Options))!;
        await admin.PostAsJsonAsync($"/api/students/{student.Id}/guardians", new LinkGuardianToStudent.Request(guardian.Id, "anne", true));

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Istanbul).Date);
        var term = ReportTerm.Containing(today);
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Teacher.Id, piano.Id, term.Start)))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
        return new Seeded(student.Id, enrollment.Id, teacher.Teacher.Id, piano.Id, teacherEmail, teacher.TemporaryPassword!, phone, term);
    }

    // Dönemin içinde (bugün dönemdeyse bugün, Temmuz-Ağustos'taysa dönemin son günü) dersler,
    // yoklamalar, bir eser notu, iki yetenek puanı ve bir pratik kaydı.
    private async Task<Guid> SeedTermActivityAsync(Seeded seeded)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Istanbul).Date);
        var day = today < seeded.Term.Start ? seeded.Term.Start : today > seeded.Term.End ? seeded.Term.End : today;
        var now = DateTimeOffset.UtcNow;
        await using var db = await _factory.CreateDbContextAsync();

        Lesson Completed(int hour, AttendanceStatus status)
        {
            var start = LessonGenerator.ToUtcInstant(day, new TimeOnly(hour, 0), Istanbul);
            var lesson = Lesson.CreateMakeup(seeded.StudentId, seeded.TeacherId, seeded.InstrumentId, start, start.AddMinutes(45), now);
            lesson.Complete(start.AddHours(1));
            db.Lessons.Add(lesson);
            db.LessonAttendances.Add(LessonAttendance.Create(lesson.Id, status, seeded.TeacherId, null, start.AddHours(1)));
            return lesson;
        }
        var first = Completed(9, AttendanceStatus.Present);
        Completed(10, AttendanceStatus.Present);
        Completed(11, AttendanceStatus.Excused);
        await db.SaveChangesAsync();

        db.LessonNotes.Add(LessonNote.Create(first.Id, seeded.TeacherId, "Gam", null, null, null, "Minuet in G", null, now,
            pieceComposer: "Bach", pieceStatus: RepertoireStatus.Polishing));
        var rhythmId = await db.SkillDefinitions.Where(s => s.Code == "RHYTHM").Select(s => s.Id).SingleAsync();
        var dayStart = LessonGenerator.ToUtcInstant(day, new TimeOnly(8, 0), Istanbul);
        db.SkillAssessments.Add(SkillAssessment.Create(seeded.StudentId, rhythmId, seeded.TeacherId, null, 2, null, dayStart));
        db.SkillAssessments.Add(SkillAssessment.Create(seeded.StudentId, rhythmId, seeded.TeacherId, null, 4, null, dayStart.AddMinutes(30)));
        db.PracticeJournalEntries.Add(PracticeJournalEntry.Create(seeded.StudentId, day, 25, "Gam", null, Guid.NewGuid(), now));
        await db.SaveChangesAsync();
        return first.Id;
    }

    [Fact]
    public async Task Teacher_writes_and_publishes_a_term_report_that_guardians_see_with_frozen_facts()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedAsync(admin, "a");
        await SeedTermActivityAsync(seeded);
        var teacher = await LoginTeacherAsync(seeded.TeacherEmail, seeded.TeacherPassword);
        var guardian = await LoginGuardianAsync(seeded.GuardianPhone);
        var url = $"/api/enrollments/{seeded.EnrollmentId}/term-reports/{seeded.Term.Key}";

        var list = (await teacher.GetFromJsonAsync<TermReports.ListResponse>("/api/term-reports", TestJson.Options))!;
        Assert.Equal(seeded.Term.Key, list.Term.Key);
        var row = Assert.Single(list.Rows);
        Assert.Equal(seeded.EnrollmentId, row.EnrollmentId);
        Assert.Null(row.Status);

        // Taslak: veriler canlı hesaplanır.
        var draft = (await (await teacher.PutAsJsonAsync(url, new TermReports.SaveRequest("  Bu dönem ritimde çok ilerledi.  ")))
            .Content.ReadFromJsonAsync<TermReports.ReportResponse>(TestJson.Options))!;
        Assert.Equal(TermReportStatus.Draft, draft.Status);
        Assert.Equal("Bu dönem ritimde çok ilerledi.", draft.Comment);
        Assert.Equal(2, draft.Facts.LessonsPresent);
        Assert.Equal(1, draft.Facts.LessonsExcused);
        Assert.Equal(0, draft.Facts.LessonsAbsent);
        var piece = Assert.Single(draft.Facts.Pieces);
        Assert.Equal("Minuet in G", piece.Title);
        Assert.Equal("Bach", piece.Composer);
        var skill = Assert.Single(draft.Facts.Skills);
        Assert.Equal(("Ritim", 2, 4), (skill.Label, skill.FirstScore, skill.LastScore));
        Assert.Equal(25, draft.Facts.PracticeMinutes);

        // Taslak veliye görünmez.
        var guardianUrl = $"/api/guardian/me/students/{seeded.StudentId}/term-reports";
        Assert.Empty((await guardian.GetFromJsonAsync<List<TermReports.GuardianReport>>(guardianUrl, TestJson.Options))!);

        var published = (await (await teacher.PostAsync($"{url}/publish", null))
            .Content.ReadFromJsonAsync<TermReports.ReportResponse>(TestJson.Options))!;
        Assert.Equal(TermReportStatus.Published, published.Status);
        Assert.NotNull(published.PublishedAt);

        var guardianReports = (await guardian.GetFromJsonAsync<List<TermReports.GuardianReport>>(guardianUrl, TestJson.Options))!;
        var guardianReport = Assert.Single(guardianReports);
        Assert.Equal("Bu dönem ritimde çok ilerledi.", guardianReport.Comment);
        Assert.Equal(2, guardianReport.Facts.LessonsPresent);

        // Yayından sonra girilen bir yoklama yayınlanmış karneyi değiştirmez.
        await SeedTermActivityAsync(seeded);
        var frozen = (await teacher.GetFromJsonAsync<TermReports.ReportResponse>(url, TestJson.Options))!;
        Assert.Equal(2, frozen.Facts.LessonsPresent);

        // Yayındaki karne düzenlenemez; yayından kaldırılınca veliden düşer ve veriler tazelenir.
        Assert.Equal(HttpStatusCode.Conflict, (await teacher.PutAsJsonAsync(url, new TermReports.SaveRequest("değişti"))).StatusCode);
        var unpublished = (await (await teacher.PostAsync($"{url}/unpublish", null))
            .Content.ReadFromJsonAsync<TermReports.ReportResponse>(TestJson.Options))!;
        Assert.Equal(TermReportStatus.Draft, unpublished.Status);
        Assert.Equal(4, unpublished.Facts.LessonsPresent);
        Assert.Empty((await guardian.GetFromJsonAsync<List<TermReports.GuardianReport>>(guardianUrl, TestJson.Options))!);

        var adminList = (await admin.GetFromJsonAsync<TermReports.ListResponse>("/api/term-reports", TestJson.Options))!;
        Assert.Equal(TermReportStatus.Draft, adminList.Rows.Single(r => r.EnrollmentId == seeded.EnrollmentId).Status);
    }

    [Fact]
    public async Task Term_report_rules_reject_other_teachers_empty_comments_future_terms_and_unlinked_guardians()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedAsync(admin, "b");
        var other = await SeedAsync(admin, "c");
        var otherTeacher = await LoginTeacherAsync(other.TeacherEmail, other.TeacherPassword);
        var teacher = await LoginTeacherAsync(seeded.TeacherEmail, seeded.TeacherPassword);
        var url = $"/api/enrollments/{seeded.EnrollmentId}/term-reports/{seeded.Term.Key}";

        Assert.Equal(HttpStatusCode.Forbidden, (await otherTeacher.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherTeacher.PutAsJsonAsync(url, new TermReports.SaveRequest("x"))).StatusCode);

        // Yorumsuz karne yayınlanamaz.
        await teacher.PutAsJsonAsync(url, new TermReports.SaveRequest("   "));
        Assert.Equal(HttpStatusCode.BadRequest, (await teacher.PostAsync($"{url}/publish", null)).StatusCode);

        var nextTerm = seeded.Term.Number == 1 ? new ReportTerm(seeded.Term.StartYear, 2) : new ReportTerm(seeded.Term.StartYear + 1, 1);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await teacher.GetAsync($"/api/enrollments/{seeded.EnrollmentId}/term-reports/{nextTerm.Key}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await teacher.GetAsync($"/api/enrollments/{seeded.EnrollmentId}/term-reports/2025-3")).StatusCode);

        var otherGuardian = await LoginGuardianAsync(other.GuardianPhone);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await otherGuardian.GetAsync($"/api/guardian/me/students/{seeded.StudentId}/term-reports")).StatusCode);
    }
}
