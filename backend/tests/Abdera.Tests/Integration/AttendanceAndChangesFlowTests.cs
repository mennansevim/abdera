using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Attendance.Features;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Modules.Scheduling.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// docs/00-master-prompt.md kabul kriterleri: "a teacher can record attendance and a short
// lesson note... a teacher can request a lesson change... an administrator can approve or
// reject it... original lesson history is preserved."
public class AttendanceAndChangesFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public AttendanceAndChangesFlowTests(AbderaWebApplicationFactory factory)
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

    private record SeededLesson(Guid LessonId, Guid StudentId, Guid TeacherId, Guid GuardianId, string TeacherEmail, string TeacherTempPassword);

    // Her testin ihtiyaç duyduğu tam zinciri kurar: enstrüman -> öğretmen(giriş hesaplı) ->
    // öğrenci -> veli -> kayıt -> ders serisi -> üretilen ilk ders.
    private static async Task<SeededLesson> SeedLessonAsync(HttpClient admin, string suffix)
    {
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        var teacherEmail = $"teacher-{suffix}@test.local";
        var teacherCreate = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest($"Öğretmen{suffix}", "Soyad", [piano.Id], teacherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;

        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest($"Öğrenci{suffix}", "Soyad", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;

        // Telefon numarası E.164 formatına normalize edildiği için suffix'ten sayısal bir
        // numara türetiyoruz (ham suffix harf içerdiğinden doğrudan kullanılamaz).
        var phoneDigits = (Math.Abs(suffix.GetHashCode()) % 10_000_000).ToString("D7");
        var guardian = (await (await admin.PostAsJsonAsync("/api/guardians",
                new Guardians.CreateRequest($"Veli{suffix}", "Soyad", $"0555{phoneDigits}")))
            .Content.ReadFromJsonAsync<Guardians.GuardianResponse>(TestJson.Options))!;

        await admin.PostAsJsonAsync($"/api/students/{student.Id}/guardians",
            new LinkGuardianToStudent.Request(guardian.Id, "anne", true));

        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacherCreate.Teacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        // Her test bağımsız bir gün kullanır ki üretilen dersler farklı testler arasında çakışmasın.
        var dayOfWeek = suffix.GetHashCode() % 2 == 0 ? DayOfWeek.Monday : DayOfWeek.Wednesday;
        var seriesResponse = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, dayOfWeek, new TimeOnly(17, 0), 45, DateOnly.FromDateTime(DateTime.UtcNow), null));
        var created = (await seriesResponse.Content.ReadFromJsonAsync<LessonSeriesFeatures.CreateResponse>(TestJson.Options))!;
        Assert.True(created.Generation.Created > 0);

        var lessonsResponse = await admin.GetAsync(
            $"/api/calendar?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(90).ToString("O"))}&teacherId={teacherCreate.Teacher.Id}");
        var lessons = await lessonsResponse.Content.ReadFromJsonAsync<List<Calendar.LessonResponse>>(TestJson.Options);
        var lesson = lessons!.OrderBy(l => l.StartAt).First();

        return new SeededLesson(lesson.Id, student.Id, teacherCreate.Teacher.Id, guardian.Id, teacherEmail, teacherCreate.TemporaryPassword!);
    }

    [Fact]
    public async Task Teacher_marks_attendance_and_lesson_becomes_completed()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "att1");

        using var teacherClient = _factory.CreateClient();
        var login = await teacherClient.PostAsJsonAsync("/api/auth/login", new Login.Request(seeded.TeacherEmail, seeded.TeacherTempPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var markResponse = await teacherClient.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/attendance",
            new MarkAttendance.MarkRequest(AttendanceStatus.Present, "iyi gidiyor"));
        Assert.Equal(HttpStatusCode.Created, markResponse.StatusCode);

        var lesson = await db.Lessons.SingleAsync(l => l.Id == seeded.LessonId);
        Assert.Equal(LessonStatus.Completed, lesson.Status);

        // Ders notu da ekleyebilmeli (aynı öğretmen, kendi dersi).
        var noteResponse = await teacherClient.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/notes",
            new Abdera.Api.Modules.Progress.Features.LessonNotes.CreateRequest("gam çalışması", "iyi", "günde 15 dk", "hızlanma"));
        Assert.Equal(HttpStatusCode.Created, noteResponse.StatusCode);
    }

    [Fact]
    public async Task Other_teacher_cannot_mark_attendance_for_unassigned_lesson()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "att2");

        // İkinci, ilgisiz bir öğretmen oluştur.
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var guitar = instruments!.Single(i => i.Code == "GUITAR");
        const string otherEmail = "teacher-att2-other@test.local";
        var other = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Diğer", "Öğretmen", [guitar.Id], otherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;

        using var otherClient = _factory.CreateClient();
        await otherClient.PostAsJsonAsync("/api/auth/login", new Login.Request(otherEmail, other.TemporaryPassword!));

        var response = await otherClient.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/attendance",
            new MarkAttendance.MarkRequest(AttendanceStatus.Present, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Change_request_approval_preserves_original_lesson_and_creates_rescheduled_copy()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "chg1");

        var originalLesson = await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == seeded.LessonId);
        var proposedStart = originalLesson.StartAt.AddDays(1);
        var proposedEnd = proposedStart.AddMinutes(45);

        var createResponse = await admin.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/change-requests",
            new ChangeRequests.CreateRequest("öğretmen izinli", proposedStart, proposedEnd));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var changeRequest = (await createResponse.Content.ReadFromJsonAsync<ChangeRequests.ChangeRequestResponse>(TestJson.Options))!;

        var approveResponse = await admin.PostAsync($"/api/change-requests/{changeRequest.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = (await approveResponse.Content.ReadFromJsonAsync<ChangeRequests.ApproveResponse>(TestJson.Options))!;

        var original = await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == seeded.LessonId);
        var rescheduled = await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == approved.NewLessonId);

        Assert.Equal(LessonStatus.Rescheduled, original.Status);
        Assert.Equal(LessonStatus.Normal, rescheduled.Status);
        Assert.Equal(original.Id, rescheduled.OriginalLessonId);
        Assert.Equal(proposedStart, rescheduled.StartAt);
    }

    [Fact]
    public async Task Change_request_rejection_leaves_lesson_untouched()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "chg2");

        var originalLesson = await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == seeded.LessonId);
        var proposedStart = originalLesson.StartAt.AddDays(1);

        var createResponse = await admin.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/change-requests",
            new ChangeRequests.CreateRequest(null, proposedStart, proposedStart.AddMinutes(45)));
        var changeRequest = (await createResponse.Content.ReadFromJsonAsync<ChangeRequests.ChangeRequestResponse>(TestJson.Options))!;

        var rejectResponse = await admin.PostAsync($"/api/change-requests/{changeRequest.Id}/reject", null);
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);

        var lesson = await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == seeded.LessonId);
        Assert.Equal(LessonStatus.Normal, lesson.Status);
    }

    [Fact]
    public async Task Cancelling_at_least_24_hours_before_earns_makeup_credit_and_credit_can_be_used()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "cxl1");

        var lesson = await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == seeded.LessonId);
        // Test verisi en az birkaç gün ileride üretildiği için (rolling window) ≥24 saat garanti.
        Assert.True((lesson.StartAt - DateTimeOffset.UtcNow).TotalHours >= 24);

        var cancelResponse = await admin.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/cancel",
            new CancelLesson.Request(CancelLesson.CancelledBy.Guardian, "hasta"));
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelResult = (await cancelResponse.Content.ReadFromJsonAsync<CancelLesson.Response>(TestJson.Options))!;
        Assert.True(cancelResult.MakeupCreditEarned);

        var credits = await (await admin.GetAsync($"/api/students/{seeded.StudentId}/makeup-credits"))
            .Content.ReadFromJsonAsync<List<MakeupCredits.CreditResponse>>(TestJson.Options);
        var credit = credits!.Single();
        Assert.Equal(MakeupCreditStatus.Available, credit.Status);
        Assert.Equal(MakeupCreditEarnedReason.GuardianCancelled24H, credit.EarnedReason);

        var useResponse = await admin.PostAsJsonAsync($"/api/makeup-credits/{credit.Id}/use",
            new MakeupCredits.UseRequest(seeded.TeacherId, (await GetPianoIdAsync(admin)), DateTimeOffset.UtcNow.AddDays(3), 45));
        Assert.Equal(HttpStatusCode.OK, useResponse.StatusCode);

        var usedCredit = await db.MakeupCredits.AsNoTracking().SingleAsync(c => c.Id == credit.Id);
        Assert.Equal(MakeupCreditStatus.Used, usedCredit.Status);
        Assert.NotNull(usedCredit.UsedLessonId);

        var makeupLesson = await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == usedCredit.UsedLessonId);
        Assert.Equal(LessonStatus.Makeup, makeupLesson.Status);
        Assert.Null(makeupLesson.LessonSeriesId);
    }

    [Fact]
    public async Task Guardian_cancelling_less_than_24_hours_before_earns_no_credit()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "cxl2");

        // Dersin start_at'ini teste özel olarak 12 saat sonrasına çekiyoruz (<24 saat kuralı için).
        var lesson = await db.Lessons.SingleAsync(l => l.Id == seeded.LessonId);
        var nearStart = DateTimeOffset.UtcNow.AddHours(12);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE lessons SET start_at = {nearStart}, end_at = {nearStart.AddMinutes(45)} WHERE id = {seeded.LessonId}");

        var cancelResponse = await admin.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/cancel",
            new CancelLesson.Request(CancelLesson.CancelledBy.Guardian, "son dakika"));
        var result = (await cancelResponse.Content.ReadFromJsonAsync<CancelLesson.Response>(TestJson.Options))!;

        Assert.False(result.MakeupCreditEarned);
        Assert.Empty(await db.MakeupCredits.Where(c => c.StudentId == seeded.StudentId).ToListAsync());
    }

    [Fact]
    public async Task School_cancellation_always_earns_credit_regardless_of_notice()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "cxl3");

        var nearStart = DateTimeOffset.UtcNow.AddHours(2);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE lessons SET start_at = {nearStart}, end_at = {nearStart.AddMinutes(45)} WHERE id = {seeded.LessonId}");

        var cancelResponse = await admin.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/cancel",
            new CancelLesson.Request(CancelLesson.CancelledBy.School, "öğretmen hastalandı"));
        var result = (await cancelResponse.Content.ReadFromJsonAsync<CancelLesson.Response>(TestJson.Options))!;

        Assert.True(result.MakeupCreditEarned);
    }

    [Fact]
    public async Task Rsvp_can_only_be_set_for_a_guardian_linked_to_the_students_lesson()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "rsvp1");

        var okResponse = await admin.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/rsvp",
            new Rsvp.SetRequest(seeded.GuardianId, RsvpResponse.Attending));
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);

        var unrelatedGuardian = (await (await admin.PostAsJsonAsync("/api/guardians",
                new Guardians.CreateRequest("İlgisiz", "Veli", "05559998877")))
            .Content.ReadFromJsonAsync<Guardians.GuardianResponse>(TestJson.Options))!;

        var badResponse = await admin.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/rsvp",
            new Rsvp.SetRequest(unrelatedGuardian.Id, RsvpResponse.Attending));
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);
    }

    private static async Task<Guid> GetPianoIdAsync(HttpClient admin)
    {
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        return instruments!.Single(i => i.Code == "PIANO").Id;
    }
}
