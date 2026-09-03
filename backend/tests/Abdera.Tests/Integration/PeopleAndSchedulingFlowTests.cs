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

        var overviewResponse = await admin.GetAsync("/api/teachers/overview");
        overviewResponse.EnsureSuccessStatusCode();
        var overview = await overviewResponse.Content.ReadFromJsonAsync<List<Teachers.TeacherOverviewResponse>>(TestJson.Options);
        Assert.Contains(overview!, item => item.Teacher.Id == teacher.Id &&
            item.Students.Any(assigned => assigned.StudentId == student.Id && assigned.InstrumentName == piano.Name));

        var newStudentResponse = await admin.PostAsJsonAsync($"/api/teachers/{teacher.Id}/students",
            new Teachers.CreateStudentRequest("Ela", "Kaya", new DateOnly(2017, 4, 12), piano.Id, new DateOnly(2026, 8, 1)));
        Assert.Equal(HttpStatusCode.Created, newStudentResponse.StatusCode);
        var newStudent = await newStudentResponse.Content.ReadFromJsonAsync<Teachers.TeacherStudentResponse>(TestJson.Options);
        Assert.Equal("Ela", newStudent!.FirstName);
        Assert.True(await db.Enrollments.AnyAsync(item => item.StudentId == newStudent.StudentId && item.TeacherId == teacher.Id));

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
    public async Task Student_cannot_have_more_than_four_recurring_lessons_per_week()
    {
        var admin = await CreateAdminClientAsync();
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Dört", "Ders", [piano.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Haftalık", "Sınır", new DateOnly(2015, 5, 5))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday };
        foreach (var day in days)
        {
            var response = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
                enrollment.Id, day, new TimeOnly(14, 0), 45, new DateOnly(2026, 8, 24), null));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var fifth = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, DayOfWeek.Friday, new TimeOnly(14, 0), 45, new DateOnly(2026, 8, 24), null));

        Assert.Equal(HttpStatusCode.BadRequest, fifth.StatusCode);
        Assert.Contains("haftada en fazla 4", await fifth.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_can_remove_enrollment_and_future_recurring_lessons_are_stopped()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var violin = instruments!.Single(i => i.Code == "VIOLIN");

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Kurs", "Silme", [violin.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Arşiv", "Öğrenci", new DateOnly(2014, 2, 2))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, violin.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var seriesResponse = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, DayOfWeek.Saturday, new TimeOnly(12, 0), 45, new DateOnly(2026, 8, 29), null));
        Assert.Equal(HttpStatusCode.Created, seriesResponse.StatusCode);
        var series = (await seriesResponse.Content.ReadFromJsonAsync<LessonSeriesFeatures.CreateResponse>(TestJson.Options))!.Series;

        var deleteResponse = await admin.DeleteAsync($"/api/students/{student.Id}/enrollments/{enrollment.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        db.ChangeTracker.Clear();
        Assert.Equal(Abdera.Api.Modules.People.Domain.EnrollmentStatus.Ended,
            (await db.Enrollments.SingleAsync(item => item.Id == enrollment.Id)).Status);
        Assert.Equal(Abdera.Api.Modules.Scheduling.Domain.LessonSeriesStatus.Ended,
            (await db.LessonSeries.SingleAsync(item => item.Id == series.Id)).Status);
        Assert.Empty(await db.Lessons.Where(item => item.LessonSeriesId == series.Id && item.StartAt > DateTimeOffset.UtcNow).ToListAsync());
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

    // Öğretmenler sayfasındaki "uygun günler" tek-tık aç/kapa arayüzü bu üç uca dayanır.
    // docs/07-api.md: uygunluk tanımlamak Admin işi, öğretmen yalnızca kendi uygunluğunu
    // görebilir - TeacherAvailabilities.cs üstündeki yorumla aynı kural, burada uçtan uca
    // doğrulanıyor (yalnızca entity seviyesinde değil).
    [Fact]
    public async Task Admin_toggles_teacher_availability_days_and_teacher_can_only_read()
    {
        var admin = await CreateAdminClientAsync();
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var violin = instruments!.Single(i => i.Code == "VIOLIN");

        var teacherEmail = $"avail-{Guid.NewGuid():N}@test.local";
        var teacherCreate = await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Uygunluk", "Testi", [violin.Id], teacherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options);
        var teacherId = teacherCreate!.Teacher.Id;

        // Başlangıçta hiç kayıt yok - "gün seçilmediyse her gün uygun sayılır" varsayımının
        // dayandığı boş liste hâli.
        var initialList = await admin.GetFromJsonAsync<List<TeacherAvailabilities.AvailabilityResponse>>(
            $"/api/teachers/{teacherId}/availability", TestJson.Options);
        Assert.Empty(initialList!);

        // Salı ve Perşembe'yi "aç" - iki ayrı POST, arayüzdeki iki ayrı tık gibi.
        var tuesday = await admin.PostAsJsonAsync($"/api/teachers/{teacherId}/availability",
            new TeacherAvailabilities.CreateRequest(DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(19, 0)));
        Assert.Equal(HttpStatusCode.Created, tuesday.StatusCode);
        var tuesdayAvailability = await tuesday.Content.ReadFromJsonAsync<TeacherAvailabilities.AvailabilityResponse>(TestJson.Options);

        var thursday = await admin.PostAsJsonAsync($"/api/teachers/{teacherId}/availability",
            new TeacherAvailabilities.CreateRequest(DayOfWeek.Thursday, new TimeOnly(9, 0), new TimeOnly(19, 0)));
        Assert.Equal(HttpStatusCode.Created, thursday.StatusCode);

        var afterCreate = await admin.GetFromJsonAsync<List<TeacherAvailabilities.AvailabilityResponse>>(
            $"/api/teachers/{teacherId}/availability", TestJson.Options);
        Assert.Equal(2, afterCreate!.Count);
        Assert.Contains(afterCreate, a => a.DayOfWeek == DayOfWeek.Tuesday);
        Assert.Contains(afterCreate, a => a.DayOfWeek == DayOfWeek.Thursday);

        // Öğretmen kendi uygunluğunu görebilir ama değiştiremez.
        using var teacherClient = _factory.CreateClient();
        var teacherLogin = await teacherClient.PostAsJsonAsync("/api/auth/login",
            new Login.Request(teacherEmail, teacherCreate.TemporaryPassword!));
        Assert.Equal(HttpStatusCode.OK, teacherLogin.StatusCode);

        var teacherRead = await teacherClient.GetAsync($"/api/teachers/{teacherId}/availability");
        Assert.Equal(HttpStatusCode.OK, teacherRead.StatusCode);

        var teacherCreateAttempt = await teacherClient.PostAsJsonAsync($"/api/teachers/{teacherId}/availability",
            new TeacherAvailabilities.CreateRequest(DayOfWeek.Friday, new TimeOnly(9, 0), new TimeOnly(19, 0)));
        Assert.Equal(HttpStatusCode.Forbidden, teacherCreateAttempt.StatusCode);

        var teacherDeleteAttempt = await teacherClient.DeleteAsync($"/api/teachers/{teacherId}/availability/{tuesdayAvailability!.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, teacherDeleteAttempt.StatusCode);

        // Admin Salı'yı "kapatır" - tek tıkla kapama = uygunluk kaydını silme.
        var deleteResponse = await admin.DeleteAsync($"/api/teachers/{teacherId}/availability/{tuesdayAvailability.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var afterDelete = await admin.GetFromJsonAsync<List<TeacherAvailabilities.AvailabilityResponse>>(
            $"/api/teachers/{teacherId}/availability", TestJson.Options);
        Assert.Single(afterDelete!);
        Assert.Equal(DayOfWeek.Thursday, afterDelete!.Single().DayOfWeek);

        // Aynı kaydı ikinci kez silmeye çalışmak (çift tık/ağ tekrarı) kontrollü 404 vermeli.
        var deleteAgain = await admin.DeleteAsync($"/api/teachers/{teacherId}/availability/{tuesdayAvailability.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }

    // Öğrenciler listesindeki enstrüman rozetleri /api/students/overview'a dayanır. Kritik
    // izolasyon kuralı: bir öğrenci iki farklı öğretmenden ders alıyorsa, öğretmen scope'unda
    // yalnızca KENDİ kursu görünmeli - Enrollments.cs ListAsync'teki kuralla birebir aynı.
    [Fact]
    public async Task Student_overview_shows_instrument_badges_and_scopes_them_per_teacher()
    {
        var admin = await CreateAdminClientAsync();
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");
        var violin = instruments!.Single(i => i.Code == "VIOLIN");

        var pianoTeacherEmail = $"overview-piano-{Guid.NewGuid():N}@test.local";
        var pianoTeacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Piyano", "Hoca", [piano.Id], pianoTeacherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;
        var violinTeacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Keman", "Hoca", [violin.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;

        var sharedStudent = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Paylaşılan", "Öğrenci", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;

        await admin.PostAsJsonAsync($"/api/students/{sharedStudent.Id}/enrollments",
            new Enrollments.CreateRequest(pianoTeacher.Teacher.Id, piano.Id, new DateOnly(2026, 8, 1)));
        await admin.PostAsJsonAsync($"/api/students/{sharedStudent.Id}/enrollments",
            new Enrollments.CreateRequest(violinTeacher.Id, violin.Id, new DateOnly(2026, 8, 1)));

        // Admin: ikisini de görür.
        var adminOverview = await admin.GetFromJsonAsync<List<Students.StudentOverviewResponse>>(
            "/api/students/overview", TestJson.Options);
        var adminRow = adminOverview!.Single(row => row.Student.Id == sharedStudent.Id);
        Assert.Equal(2, adminRow.Instruments.Count);
        Assert.Contains(adminRow.Instruments, i => i.InstrumentName == piano.Name);
        Assert.Contains(adminRow.Instruments, i => i.InstrumentName == violin.Name);

        // Piyano öğretmeni: yalnızca kendi kursunu (Piyano) görür, Keman sızmaz.
        using var teacherClient = _factory.CreateClient();
        (await teacherClient.PostAsJsonAsync("/api/auth/login",
            new Login.Request(pianoTeacherEmail, pianoTeacher.TemporaryPassword!))).EnsureSuccessStatusCode();

        var teacherOverview = await teacherClient.GetFromJsonAsync<List<Students.StudentOverviewResponse>>(
            "/api/students/overview", TestJson.Options);
        var teacherRow = teacherOverview!.Single(row => row.Student.Id == sharedStudent.Id);
        Assert.Single(teacherRow.Instruments);
        Assert.Equal(piano.Name, teacherRow.Instruments.Single().InstrumentName);
    }
}
