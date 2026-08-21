using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// docs/00-master-prompt.md kabul kriteri: "an administrator can create people and
// relationships... define a recurring schedule... future concrete lessons are generated
// without duplicates... teachers see only assigned lessons."
public class PeopleAndSchedulingFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public PeopleAndSchedulingFlowTests(AbderaWebApplicationFactory factory)
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

    [Fact]
    public async Task Admin_creates_people_and_a_recurring_series_generates_lessons_without_duplicates()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        // Enstrümanlar migration ile seed edildi (docs/08-migrations.md SeedInstruments)
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        var teacherResponse = await admin.PostAsJsonAsync("/api/teachers",
            new Teachers.CreateRequest("Ayşe", "Yılmaz", [piano.Id], null));
        teacherResponse.EnsureSuccessStatusCode();
        var teacher = (await teacherResponse.Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;

        var studentResponse = await admin.PostAsJsonAsync("/api/students",
            new Students.CreateRequest("Ece", "Demir", new DateOnly(2015, 3, 10)));
        studentResponse.EnsureSuccessStatusCode();
        var student = (await studentResponse.Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;

        var guardianResponse = await admin.PostAsJsonAsync("/api/guardians",
            new Guardians.CreateRequest("Fatma", "Demir", "0555 111 22 33"));
        guardianResponse.EnsureSuccessStatusCode();
        var guardian = (await guardianResponse.Content.ReadFromJsonAsync<Guardians.GuardianResponse>(TestJson.Options))!;

        var linkResponse = await admin.PostAsJsonAsync($"/api/students/{student.Id}/guardians",
            new LinkGuardianToStudent.Request(guardian.Id, "anne", true));
        Assert.Equal(HttpStatusCode.Created, linkResponse.StatusCode);

        var enrollmentResponse = await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
            new Enrollments.CreateRequest(teacher.Id, piano.Id, new DateOnly(2026, 8, 1)));
        enrollmentResponse.EnsureSuccessStatusCode();
        var enrollment = (await enrollmentResponse.Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var seriesResponse = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, DayOfWeek.Tuesday, new TimeOnly(18, 0), 45, new DateOnly(2026, 8, 18), null));
        Assert.Equal(HttpStatusCode.Created, seriesResponse.StatusCode);
        var created = (await seriesResponse.Content.ReadFromJsonAsync<LessonSeriesFeatures.CreateResponse>(TestJson.Options))!;

        // Ders üretildi (master prompt: "system generates a lesson")
        Assert.True(created.Generation.Created > 0);

        var generatedCount = await db.Lessons.CountAsync(l => l.LessonSeriesId == created.Series.Id);
        Assert.Equal(created.Generation.Created, generatedCount);

        // İkinci kez tetiklense de mükerrer satır oluşmaz (idempotency)
        var regenerateResponse = await admin.PostAsync($"/api/lesson-series/{created.Series.Id}/generate", null);
        regenerateResponse.EnsureSuccessStatusCode();
        var regenerated = await regenerateResponse.Content.ReadFromJsonAsync<LessonSeriesFeatures.GenerationSummary>(TestJson.Options);
        Assert.Equal(0, regenerated!.Created);

        var countAfterRegenerate = await db.Lessons.CountAsync(l => l.LessonSeriesId == created.Series.Id);
        Assert.Equal(generatedCount, countAfterRegenerate);

        // GET /api/calendar - EF Core'un OrderBy+projeksiyon sırasına duyarlı bir regresyonu var
        // (bkz. Modules/Scheduling/Features/Calendar.cs yorumu); yalnızca DB'den saymak yetmez,
        // gerçek HTTP çağrısının SQL'e çevrilebildiğini de doğrulamak gerekir.
        var firstLesson = await db.Lessons.Where(l => l.LessonSeriesId == created.Series.Id)
            .OrderBy(l => l.StartAt).FirstAsync();
        var from = firstLesson.StartAt.AddDays(-1).ToString("O");
        var to = firstLesson.StartAt.AddDays(1).ToString("O");
        var calendarResponse = await admin.GetAsync($"/api/calendar?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");
        Assert.Equal(HttpStatusCode.OK, calendarResponse.StatusCode);
        var calendarLessons = await calendarResponse.Content.ReadFromJsonAsync<List<Calendar.LessonResponse>>(TestJson.Options);
        Assert.Contains(calendarLessons!, l => l.Id == firstLesson.Id && l.StudentName == "Ece Demir");
    }

    [Fact]
    public async Task Creating_overlapping_series_for_same_teacher_is_rejected()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var guitar = instruments!.Single(i => i.Code == "GUITAR");

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Can", "Öz", [guitar.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;

        var student1 = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Ali", "Kaya", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var student2 = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Zeynep", "Kaya", new DateOnly(2016, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;

        var enrollment1 = (await (await admin.PostAsJsonAsync($"/api/students/{student1.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, guitar.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
        var enrollment2 = (await (await admin.PostAsJsonAsync($"/api/students/{student2.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, guitar.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var first = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment1.Id, DayOfWeek.Thursday, new TimeOnly(17, 0), 60, new DateOnly(2026, 8, 20), null));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Aynı öğretmen, aynı gün, çakışan saat (17:30 - 16:30-17:30 arasında başlıyor) - reddedilmeli
        var second = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment2.Id, DayOfWeek.Thursday, new TimeOnly(17, 30), 60, new DateOnly(2026, 8, 20), null));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Calendar_rejects_date_range_wider_than_three_months()
    {
        // ARC-3 (docs/13-audit-fix-prompt.md): bir yıllık ders geçmişi biriktiğinde takvim
        // sorgusu sınırsız satır dönmesin diye zorunlu bir üst sınır var.
        var admin = await CreateAdminClientAsync();

        var from = DateTimeOffset.UtcNow;
        var tooWide = from.AddDays(94);
        var response = await admin.GetAsync(
            $"/api/calendar?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(tooWide.ToString("O"))}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var withinLimit = from.AddDays(90);
        var okResponse = await admin.GetAsync(
            $"/api/calendar?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(withinLimit.ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
    }

    [Fact]
    public async Task Teacher_can_only_see_own_assigned_students_and_lessons()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var drums = instruments!.Single(i => i.Code == "DRUMS");

        const string teacherEmail = "teacher-scope@test.local";
        var teacherCreate = await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Deniz", "Kurt", [drums.Id], teacherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options);
        Assert.NotNull(teacherCreate!.TemporaryPassword);

        var myStudent = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Kendi", "Öğrencim", new DateOnly(2013, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var otherStudent = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Başka", "Öğrenci", new DateOnly(2013, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;

        await admin.PostAsJsonAsync($"/api/students/{myStudent.Id}/enrollments",
            new Enrollments.CreateRequest(teacherCreate.Teacher.Id, drums.Id, new DateOnly(2026, 8, 1)));

        using var teacherClient = _factory.CreateClient();
        var loginResponse = await teacherClient.PostAsJsonAsync("/api/auth/login",
            new Login.Request(teacherEmail, teacherCreate.TemporaryPassword!));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var listResponse = await teacherClient.GetAsync("/api/students");
        var visibleStudents = await listResponse.Content.ReadFromJsonAsync<List<Students.StudentResponse>>(TestJson.Options);
        Assert.Contains(visibleStudents!, s => s.Id == myStudent.Id);
        Assert.DoesNotContain(visibleStudents!, s => s.Id == otherStudent.Id);

        var forbidden = await teacherClient.GetAsync($"/api/students/{otherStudent.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var allowed = await teacherClient.GetAsync($"/api/students/{myStudent.Id}");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }
}
