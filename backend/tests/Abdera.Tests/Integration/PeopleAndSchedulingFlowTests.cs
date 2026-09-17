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
        // Aynı enstrümandan ikinci bir program açılamadığı için (bkz.
        // Student_cannot_have_two_schedules_for_the_same_instrument) haftalık sınır ancak
        // farklı enstrümanlarla test edilebilir - beş ayrı branş gerekiyor.
        var branches = instruments!
            .Where(item => item.Code is "PIANO" or "GUITAR" or "VIOLIN" or "DRUMS" or "CELLO")
            .OrderBy(item => item.Code)
            .ToList();
        Assert.Equal(5, branches.Count);

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Dört", "Ders", [.. branches.Select(item => item.Id)], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Haftalık", "Sınır", new DateOnly(2015, 5, 5))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;

        var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        var statuses = new List<HttpStatusCode>();
        for (var index = 0; index < branches.Count; index++)
        {
            var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                    new Enrollments.CreateRequest(teacher.Id, branches[index].Id, new DateOnly(2026, 8, 1))))
                .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

            var response = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
                enrollment.Id, days[index], new TimeOnly(14, 0), 45, new DateOnly(2026, 8, 24), null));
            statuses.Add(response.StatusCode);

            if (index == branches.Count - 1)
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Contains("haftada en fazla 4", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            }
        }

        Assert.Equal(4, statuses.Count(status => status == HttpStatusCode.Created));
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
    public async Task Admin_and_the_teacher_itself_toggle_availability_days()
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

        // Öğretmen KENDİ uygunluğunu görür ve değiştirir (docs/10-decisions.md K1) - başka
        // bir öğretmeninkine dokunamaz, o sınır TeacherPortalFlowTests'te ayrıca test ediliyor.
        using var teacherClient = _factory.CreateClient();
        var teacherLogin = await teacherClient.PostAsJsonAsync("/api/auth/login",
            new Login.Request(teacherEmail, teacherCreate.TemporaryPassword!));
        Assert.Equal(HttpStatusCode.OK, teacherLogin.StatusCode);

        var teacherRead = await teacherClient.GetAsync($"/api/teachers/{teacherId}/availability");
        Assert.Equal(HttpStatusCode.OK, teacherRead.StatusCode);

        var teacherOpensFriday = await teacherClient.PostAsJsonAsync($"/api/teachers/{teacherId}/availability",
            new TeacherAvailabilities.CreateRequest(DayOfWeek.Friday, new TimeOnly(9, 0), new TimeOnly(19, 0)));
        Assert.Equal(HttpStatusCode.Created, teacherOpensFriday.StatusCode);
        var friday = await teacherOpensFriday.Content.ReadFromJsonAsync<TeacherAvailabilities.AvailabilityResponse>(TestJson.Options);

        // Açtığı günü yine kendisi kapatabilir - aşağıdaki admin sayımları Salı/Perşembe
        // üzerinden devam etsin diye Cuma burada geri alınıyor.
        var teacherClosesFriday = await teacherClient.DeleteAsync($"/api/teachers/{teacherId}/availability/{friday!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, teacherClosesFriday.StatusCode);

        // Admin Salı'yı "kapatır" - tek tıkla kapama = uygunluk kaydını silme.
        var deleteResponse = await admin.DeleteAsync($"/api/teachers/{teacherId}/availability/{tuesdayAvailability!.Id}");
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

    // Kullanıcı geri bildirimi: "öğretmen diğer öğretmenlerin derslerini görmemeli...
    // sadece kendi branşını görebilir." /api/auth/me artık Teacher oturumunda kendi
    // TeacherInstruments'ını döndürüyor (Takvim'deki enstrüman filtresi bunu kullanır);
    // Admin'de her zaman boş - bu bilgiye ihtiyacı yok, kısıtlaması da yok.
    [Fact]
    public async Task Me_returns_teachers_own_instruments_and_empty_for_admin()
    {
        var admin = await CreateAdminClientAsync();
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");
        var guitar = instruments!.Single(i => i.Code == "GUITAR");

        var teacherEmail = $"me-instruments-{Guid.NewGuid():N}@test.local";
        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Çoklu", "Enstrüman", [piano.Id, guitar.Id], teacherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;

        using var teacherClient = _factory.CreateClient();
        (await teacherClient.PostAsJsonAsync("/api/auth/login",
            new Login.Request(teacherEmail, teacher.TemporaryPassword!))).EnsureSuccessStatusCode();

        var teacherMe = await teacherClient.GetFromJsonAsync<Me.Response>("/api/auth/me", TestJson.Options);
        Assert.Equal(2, teacherMe!.InstrumentIds.Length);
        Assert.Contains(piano.Id, teacherMe.InstrumentIds);
        Assert.Contains(guitar.Id, teacherMe.InstrumentIds);

        var adminMe = await admin.GetFromJsonAsync<Me.Response>("/api/auth/me", TestJson.Options);
        Assert.Empty(adminMe!.InstrumentIds);
    }

    // Aynı ihlal iddiasının takvim tarafı: bir öğretmen, başka bir öğretmenin id'sini
    // sorgu parametresi olarak göndererek onun derslerini görmeye çalışırsa backend bunu
    // yok saymalı (AuthContext.ResolveTeacherScopeAsync zaten kendi id'sini zorluyor) -
    // yanıt boş dönmeli, başka öğretmenin dersi asla sızmamalı.
    [Fact]
    public async Task Teacher_cannot_use_teacherId_query_param_to_see_another_teachers_lessons()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");
        var guitar = instruments!.Single(i => i.Code == "GUITAR");

        var ownerEmail = $"isolation1-owner-{Guid.NewGuid():N}@test.local";
        var owner = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Sahip", "Öğretmen", [piano.Id], ownerEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var otherEmail = $"isolation1-other-{Guid.NewGuid():N}@test.local";
        var other = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Başka", "Öğretmen", [guitar.Id], otherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;

        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("İzolasyon", "Öğrencisi", new DateOnly(2015, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(owner.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var series = (await (await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
                enrollment.Id, DayOfWeek.Wednesday, new TimeOnly(17, 0), 45, new DateOnly(2026, 8, 19), null)))
            .Content.ReadFromJsonAsync<LessonSeriesFeatures.CreateResponse>(TestJson.Options))!;
        var ownerLesson = await db.Lessons.AsNoTracking()
            .Where(lesson => lesson.LessonSeriesId == series.Series.Id)
            .OrderBy(lesson => lesson.StartAt).FirstAsync();

        using var otherClient = _factory.CreateClient();
        (await otherClient.PostAsJsonAsync("/api/auth/login",
            new Login.Request(otherEmail, other.TemporaryPassword!))).EnsureSuccessStatusCode();

        var from = Uri.EscapeDataString(ownerLesson.StartAt.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(ownerLesson.StartAt.AddDays(1).ToString("O"));
        var lessons = await otherClient.GetFromJsonAsync<List<Calendar.LessonResponse>>(
            $"/api/calendar?from={from}&to={to}&teacherId={owner.Id}", TestJson.Options);

        Assert.DoesNotContain(lessons!, lesson => lesson.Id == ownerLesson.Id);
    }

    // Canlı QA turunda bulunan gerçek bug: PATCH /api/teachers/{id} yalnızca Teacher.Status
    // domain alanını değiştiriyordu, bağlı giriş hesabını (User.IsActive) hiç etkilemiyordu -
    // "pasife alınan" bir öğretmen mevcut oturumuyla (hatta yeniden giriş yaparak) sisteme
    // erişmeye devam edebiliyordu.
    [Fact]
    public async Task Deactivating_a_teacher_disables_their_login_account_and_drops_the_existing_session()
    {
        var admin = await CreateAdminClientAsync();
        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        var email = $"deactivate-{Guid.NewGuid():N}@test.local";
        var created = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Pasife", "Alınacak", [piano.Id], email)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;

        using var teacherClient = _factory.CreateClient();
        (await teacherClient.PostAsJsonAsync("/api/auth/login",
            new Login.Request(email, created.TemporaryPassword!))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await teacherClient.GetAsync("/api/students/overview")).StatusCode);

        var deactivateResponse = await admin.PatchAsJsonAsync($"/api/teachers/{created.Teacher.Id}",
            new Teachers.UpdateRequest("Pasife", "Alınacak", Abdera.Api.Modules.People.Domain.TeacherStatus.Inactive, [piano.Id]));
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        // Var olan oturum bir sonraki istekte düşmeli (Program.cs OnValidatePrincipal).
        Assert.Equal(HttpStatusCode.Unauthorized, (await teacherClient.GetAsync("/api/students/overview")).StatusCode);

        // Yeniden giriş denemesi de artık reddedilmeli - hesap tamamen kilitli.
        using var retryClient = _factory.CreateClient();
        var retryLogin = await retryClient.PostAsJsonAsync("/api/auth/login",
            new Login.Request(email, created.TemporaryPassword!));
        Assert.Equal(HttpStatusCode.Unauthorized, retryLogin.StatusCode);

        // Tekrar Active yapılınca hesap yeniden çalışmalı.
        var reactivateResponse = await admin.PatchAsJsonAsync($"/api/teachers/{created.Teacher.Id}",
            new Teachers.UpdateRequest("Pasife", "Alınacak", Abdera.Api.Modules.People.Domain.TeacherStatus.Active, [piano.Id]));
        Assert.Equal(HttpStatusCode.OK, reactivateResponse.StatusCode);

        using var reactivatedClient = _factory.CreateClient();
        var reactivatedLogin = await reactivatedClient.PostAsJsonAsync("/api/auth/login",
            new Login.Request(email, created.TemporaryPassword!));
        Assert.Equal(HttpStatusCode.OK, reactivatedLogin.StatusCode);
    }


    // Kullanıcı isteği: "öğrenci altında program görünebilir olmalı, buradan ders saatini
    // güncelleyebilmeliyim. her hafta pazartesi 18:00 piyano mesela."
    // Taşıma "eskisini kapat + yenisini aç" olarak işlenir; asıl doğrulanan şey, geçmiş
    // derslerin yerinde kalıp gelecektekilerin yeni güne taşınması.
    [Fact]
    public async Task Student_schedule_is_listed_and_can_be_moved_to_another_day()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Program", "Ogretmeni", [piano.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Program", "Ogrencisi", new DateOnly(2015, 6, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var createdResponse = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, DayOfWeek.Tuesday, new TimeOnly(18, 0), 45, new DateOnly(2026, 8, 3), null));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<LessonSeriesFeatures.CreateResponse>(TestJson.Options))!;

        // Öğrenci künyesindeki program listesi - öğretmen ve enstrüman adı aynı yanıtta gelir.
        var listed = await admin.GetFromJsonAsync<List<LessonSeriesFeatures.StudentSeriesResponse>>(
            $"/api/students/{student.Id}/lesson-series", TestJson.Options);
        var row = Assert.Single(listed!);
        Assert.Equal(DayOfWeek.Tuesday, row.DayOfWeek);
        Assert.Equal(new TimeOnly(18, 0), row.StartTime);
        Assert.Equal("Program Ogretmeni", row.TeacherName);
        Assert.Equal(piano.Name, row.InstrumentName);

        // "Her hafta Pazartesi 18:00" - programı yeni güne taşı.
        var movedResponse = await admin.PostAsJsonAsync(
            $"/api/lesson-series/{created.Series.Id}/reschedule",
            new LessonSeriesFeatures.RescheduleRequest(DayOfWeek.Monday, new TimeOnly(18, 0), 45, null));
        Assert.True(movedResponse.StatusCode == HttpStatusCode.OK, await movedResponse.Content.ReadAsStringAsync());
        var moved = (await movedResponse.Content.ReadFromJsonAsync<LessonSeriesFeatures.CreateResponse>(TestJson.Options))!;
        Assert.NotEqual(created.Series.Id, moved.Series.Id);
        Assert.True(moved.Generation.Created > 0);

        var afterMove = await admin.GetFromJsonAsync<List<LessonSeriesFeatures.StudentSeriesResponse>>(
            $"/api/students/{student.Id}/lesson-series", TestJson.Options);
        var movedRow = Assert.Single(afterMove!);
        Assert.Equal(moved.Series.Id, movedRow.Id);
        Assert.Equal(DayOfWeek.Monday, movedRow.DayOfWeek);

        // Eski seri kapandı, yeni seri yalnızca Pazartesi dersleri üretti.
        Assert.Equal(
            Abdera.Api.Modules.Scheduling.Domain.LessonSeriesStatus.Ended,
            await db.LessonSeries.Where(item => item.Id == created.Series.Id).Select(item => item.Status).SingleAsync());

        var newLessonDays = await db.Lessons
            .Where(lesson => lesson.LessonSeriesId == moved.Series.Id)
            .Select(lesson => lesson.StartAt)
            .ToListAsync();
        Assert.NotEmpty(newLessonDays);
        Assert.All(newLessonDays, startAt => Assert.Equal(DayOfWeek.Monday, startAt.ToOffset(TimeSpan.FromHours(3)).DayOfWeek));

        Assert.True(await db.AuditLogs.AnyAsync(log =>
            log.Action == "lesson_series.rescheduled" && log.EntityId == moved.Series.Id));
    }

    // Kullanıcı kuralı: "bir öğrenci aynı enstrüman için birden fazla ders alamasın, farklı
    // saatler de olsa." Enrollment kısıtı yalnızca aynı öğretmeni engelliyordu; asıl sızıntı
    // ikinci bir öğretmenle aynı enstrümandan ikinci bir program açmaktı.
    [Fact]
    public async Task Student_cannot_have_two_schedules_for_the_same_instrument()
    {
        var admin = await CreateAdminClientAsync();

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");
        var guitar = instruments!.Single(i => i.Code == "GUITAR");

        var firstTeacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Tek", "Enstruman", [piano.Id, guitar.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var secondTeacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Ikinci", "Piyanist", [piano.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Tek", "Program", new DateOnly(2015, 2, 2))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;

        var pianoEnrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(firstTeacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;

        var first = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            pianoEnrollment.Id, DayOfWeek.Monday, new TimeOnly(18, 0), 45, new DateOnly(2026, 8, 3), null));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Aynı kayıt üzerinden farklı bir güne ikinci program
        var sameEnrollmentAgain = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            pianoEnrollment.Id, DayOfWeek.Thursday, new TimeOnly(16, 0), 45, new DateOnly(2026, 8, 3), null));
        Assert.Equal(HttpStatusCode.Conflict, sameEnrollmentAgain.StatusCode);

        // Aynı enstrüman, BAŞKA öğretmen - asıl kapatılan açık bu
        var otherTeacherEnrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(secondTeacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
        var otherTeacherSeries = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            otherTeacherEnrollment.Id, DayOfWeek.Friday, new TimeOnly(14, 0), 45, new DateOnly(2026, 8, 3), null));
        Assert.Equal(HttpStatusCode.Conflict, otherTeacherSeries.StatusCode);

        // Başka bir enstrüman hâlâ serbest
        var guitarEnrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(firstTeacher.Id, guitar.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
        var guitarSeries = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            guitarEnrollment.Id, DayOfWeek.Wednesday, new TimeOnly(17, 0), 45, new DateOnly(2026, 8, 3), null));
        Assert.Equal(HttpStatusCode.Created, guitarSeries.StatusCode);
    }
}
