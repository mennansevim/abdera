using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// docs/10-decisions.md Karar F reversal: veli telefon + WhatsApp OTP ile giriş yapıp yalnızca
// kendi öğrencisinin listesini/takvimini görebilmeli, RSVP'sini kendisi ayarlayabilmeli - ve
// başka bir veliye ait öğrenci/derse asla erişememeli.
public class GuardianPortalFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public GuardianPortalFlowTests(AbderaWebApplicationFactory factory)
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

    private record SeededLesson(Guid LessonId, Guid StudentId, Guid GuardianId, string GuardianPhone, string TeacherName, string InstrumentName);

    // AttendanceAndChangesFlowTests.SeedLessonAsync ile aynı zinciri kurar, tek fark: telefon
    // numarasını teste geri döndürür (OTP isteği için gerekli).
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

        var phoneDigits = (Math.Abs(suffix.GetHashCode()) % 10_000_000).ToString("D7");
        var rawPhone = $"0555{phoneDigits}";
        var guardian = (await (await admin.PostAsJsonAsync("/api/guardians",
                new Guardians.CreateRequest($"Veli{suffix}", "Soyad", rawPhone)))
            .Content.ReadFromJsonAsync<Guardians.GuardianResponse>(TestJson.Options))!;

        await admin.PostAsJsonAsync($"/api/students/{student.Id}/guardians",
            new LinkGuardianToStudent.Request(guardian.Id, "anne", true));

        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacherCreate.Teacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var dayOfWeek = suffix.GetHashCode() % 2 == 0 ? DayOfWeek.Monday : DayOfWeek.Wednesday;
        var seriesResponse = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, dayOfWeek, new TimeOnly(17, 0), 45, DateOnly.FromDateTime(DateTime.UtcNow), null));
        var created = (await seriesResponse.Content.ReadFromJsonAsync<LessonSeriesFeatures.CreateResponse>(TestJson.Options))!;
        Assert.True(created.Generation.Created > 0);

        var lessonsResponse = await admin.GetAsync(
            $"/api/calendar?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(90).ToString("O"))}&teacherId={teacherCreate.Teacher.Id}");
        var lessons = await lessonsResponse.Content.ReadFromJsonAsync<List<Calendar.LessonResponse>>(TestJson.Options);
        var lesson = lessons!.OrderBy(l => l.StartAt).First();

        return new SeededLesson(
            lesson.Id, student.Id, guardian.Id, rawPhone,
            $"Öğretmen{suffix} Soyad", piano.Name);
    }

    private static async Task<HttpClient> LoginAsGuardianAsync(AbderaWebApplicationFactory factory, string rawPhone)
    {
        var client = factory.CreateClient();

        var otpRequest = await client.PostAsJsonAsync("/api/guardian/otp/request", new GuardianAuth.RequestOtpRequest(rawPhone));
        Assert.Equal(HttpStatusCode.OK, otpRequest.StatusCode);
        var otpBody = (await otpRequest.Content.ReadFromJsonAsync<GuardianAuth.RequestOtpResponse>(TestJson.Options))!;
        Assert.False(string.IsNullOrEmpty(otpBody.DebugCode)); // Development ortamında dolu gelmeli.

        var verify = await client.PostAsJsonAsync("/api/guardian/otp/verify", new GuardianAuth.VerifyOtpRequest(rawPhone, otpBody.DebugCode!));
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        return client;
    }

    [Fact]
    public async Task Guardian_can_otp_login_see_own_student_and_set_rsvp()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "gp1");

        using var guardianClient = await LoginAsGuardianAsync(_factory, seeded.GuardianPhone);

        var students = await (await guardianClient.GetAsync("/api/guardian/me/students"))
            .Content.ReadFromJsonAsync<List<GuardianPortal.GuardianStudentResponse>>(TestJson.Options);
        var own = students!.Single(s => s.StudentId == seeded.StudentId);
        Assert.Equal(seeded.InstrumentName, own.InstrumentName);
        Assert.Equal(seeded.TeacherName, own.TeacherName);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(90).ToString("O"));
        var calendarResponse = await guardianClient.GetAsync($"/api/guardian/me/students/{seeded.StudentId}/calendar?from={from}&to={to}");
        Assert.Equal(HttpStatusCode.OK, calendarResponse.StatusCode);
        var lessons = await calendarResponse.Content.ReadFromJsonAsync<List<GuardianPortal.GuardianLessonResponse>>(TestJson.Options);
        Assert.Contains(lessons!, l => l.Id == seeded.LessonId);

        var rsvpResponse = await guardianClient.PostAsJsonAsync(
            $"/api/guardian/me/lessons/{seeded.LessonId}/rsvp", new GuardianPortal.SetRsvpRequest(RsvpResponse.Attending));
        Assert.Equal(HttpStatusCode.OK, rsvpResponse.StatusCode);

        var rsvp = await db.LessonRsvps.AsNoTracking().SingleAsync(r => r.LessonId == seeded.LessonId && r.GuardianId == seeded.GuardianId);
        Assert.Equal(RsvpResponse.Attending, rsvp.Response);
        Assert.Equal(RsvpSource.GuardianWeb, rsvp.Source);
    }

    [Fact]
    public async Task Guardian_cannot_see_or_rsvp_for_a_student_that_is_not_theirs()
    {
        var admin = await CreateAdminClientAsync();
        var mine = await SeedLessonAsync(admin, "gp2a");
        var someoneElses = await SeedLessonAsync(admin, "gp2b");

        using var guardianClient = await LoginAsGuardianAsync(_factory, mine.GuardianPhone);

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(90).ToString("O"));
        var calendarResponse = await guardianClient.GetAsync($"/api/guardian/me/students/{someoneElses.StudentId}/calendar?from={from}&to={to}");
        Assert.Equal(HttpStatusCode.Forbidden, calendarResponse.StatusCode);

        var rsvpResponse = await guardianClient.PostAsJsonAsync(
            $"/api/guardian/me/lessons/{someoneElses.LessonId}/rsvp", new GuardianPortal.SetRsvpRequest(RsvpResponse.Attending));
        Assert.Equal(HttpStatusCode.Forbidden, rsvpResponse.StatusCode);
    }

    [Fact]
    public async Task Wrong_otp_code_is_rejected()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "gp3");

        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/guardian/otp/request", new GuardianAuth.RequestOtpRequest(seeded.GuardianPhone));

        var badVerify = await client.PostAsJsonAsync(
            "/api/guardian/otp/verify", new GuardianAuth.VerifyOtpRequest(seeded.GuardianPhone, "000000"));
        Assert.Equal(HttpStatusCode.Unauthorized, badVerify.StatusCode);

        var me = await client.GetAsync("/api/guardian/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }
}
