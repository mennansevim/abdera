using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Öğretmen artık kendi öğrencisini ekleyebiliyor, düzenleyebiliyor ve velisini
// girebiliyor; silme ise yöneticinin onayından geçiyor (docs/10-decisions.md J1/J2).
//
// Buradaki asıl risk yetki sızıntısı: "kendi öğrencisi" kuralı bir uçta unutulursa bir
// öğretmen başka bir öğretmenin öğrencisini düzenleyebilir. Bu yüzden her uç için hem
// izin verilen hem REDDEDİLEN yol ayrı ayrı doğrulanıyor.
public class TeacherPortalFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public TeacherPortalFlowTests(AbderaWebApplicationFactory factory)
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

    private Task<HttpClient> CreateAdminClientAsync() => LoginAsync("admin@test.local", "Test1234!");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Beklenmeyen durum: {(int)response.StatusCode} {response.StatusCode}, gövde: {body}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(body, TestJson.Options)!;
    }

    private record Seeded(HttpClient Client, Guid TeacherId, Guid InstrumentId, Guid SecondInstrumentId);

    private async Task<Seeded> SeedTeacherAsync(HttpClient admin, string suffix)
    {
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var piano = instruments.Single(instrument => instrument.Code == "PIANO");
        // İkinci enstrüman, "aynı öğrenci-öğretmen-enstrüman üçlüsünde tek aktif kayıt"
        // kısıtına takılmadan ikinci bir kurs açabilmek için.
        var art = instruments.Single(instrument => instrument.Code == "ART");

        var email = $"portal.{suffix}@test.local";
        var created = await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync(
            "/api/teachers", new Teachers.CreateRequest($"Portal{suffix}", "Ogretmen", [piano.Id, art.Id], email)));

        var client = await LoginAsync(email, created.TemporaryPassword!);
        return new Seeded(client, created.Teacher.Id, piano.Id, art.Id);
    }

    private static Task<HttpResponseMessage> AddStudentAsync(Seeded teacher, string name) =>
        teacher.Client.PostAsJsonAsync(
            $"/api/teachers/{teacher.TeacherId}/students",
            new Teachers.CreateStudentRequest(name, "Ogrenci", new DateOnly(2014, 1, 1), teacher.InstrumentId, new DateOnly(2026, 9, 1), CourseKind.Individual));

    [Fact]
    public async Task Me_reports_the_teacher_id_so_the_portal_can_act_on_its_own_behalf()
    {
        var admin = await CreateAdminClientAsync();
        var teacher = await SeedTeacherAsync(admin, "me");

        var me = await ReadAsync<Me.Response>(await teacher.Client.GetAsync("/api/auth/me"));
        Assert.Equal(teacher.TeacherId, me.TeacherId);

        var adminMe = await ReadAsync<Me.Response>(await admin.GetAsync("/api/auth/me"));
        Assert.Null(adminMe.TeacherId);
    }

    [Fact]
    public async Task Teacher_adds_edits_and_enrols_its_own_student()
    {
        var admin = await CreateAdminClientAsync();
        var teacher = await SeedTeacherAsync(admin, "ekle");

        var created = await ReadAsync<Teachers.TeacherStudentResponse>(await AddStudentAsync(teacher, "Kendi"));
        var studentId = created.StudentId;

        // Düzenleme
        var updated = await ReadAsync<Students.StudentResponse>(await teacher.Client.PatchAsJsonAsync(
            $"/api/students/{studentId}",
            new Students.UpdateRequest("Kendi", "Duzenlendi", new DateOnly(2013, 5, 5), StudentStatus.Active)));
        Assert.Equal("Duzenlendi", updated.LastName);

        // Kendi adına ikinci bir kurs
        var second = await teacher.Client.PostAsJsonAsync(
            $"/api/students/{studentId}/enrollments",
            new Enrollments.CreateRequest(teacher.TeacherId, teacher.SecondInstrumentId, new DateOnly(2026, 10, 1), CourseKind.Group));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        await using var db = await _factory.CreateDbContextAsync();
        Assert.Equal(2, await db.Enrollments.CountAsync(e => e.StudentId == studentId && e.TeacherId == teacher.TeacherId));
    }

    [Fact]
    public async Task Teacher_cannot_act_on_behalf_of_another_teacher()
    {
        var admin = await CreateAdminClientAsync();
        var mine = await SeedTeacherAsync(admin, "benim");
        var other = await SeedTeacherAsync(admin, "oteki");

        // Başkasının adına öğrenci eklemek
        var onBehalf = await mine.Client.PostAsJsonAsync(
            $"/api/teachers/{other.TeacherId}/students",
            new Teachers.CreateStudentRequest("Baskasi", "Adina", new DateOnly(2014, 1, 1), other.InstrumentId, new DateOnly(2026, 9, 1), CourseKind.Individual));
        Assert.Equal(HttpStatusCode.Forbidden, onBehalf.StatusCode);

        // Başkasının öğrencisini düzenlemek
        var otherStudent = await ReadAsync<Teachers.TeacherStudentResponse>(await AddStudentAsync(other, "Oteki"));
        var edit = await mine.Client.PatchAsJsonAsync(
            $"/api/students/{otherStudent.StudentId}",
            new Students.UpdateRequest("Ele", "Gecirdim", new DateOnly(2014, 1, 1), StudentStatus.Active));
        Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);

        // Başkasının öğrencisine kendini öğretmen yazarak kurs açmak
        var hijack = await mine.Client.PostAsJsonAsync(
            $"/api/students/{otherStudent.StudentId}/enrollments",
            new Enrollments.CreateRequest(mine.TeacherId, mine.InstrumentId, new DateOnly(2026, 9, 1), CourseKind.Individual));
        Assert.Equal(HttpStatusCode.Forbidden, hijack.StatusCode);
    }

    [Fact]
    public async Task Teacher_can_manage_the_guardian_of_its_own_student_but_not_others()
    {
        var admin = await CreateAdminClientAsync();
        var mine = await SeedTeacherAsync(admin, "veli");
        var other = await SeedTeacherAsync(admin, "veli2");

        var student = await ReadAsync<Teachers.TeacherStudentResponse>(await AddStudentAsync(mine, "Velili"));

        var guardian = await ReadAsync<Guardians.GuardianResponse>(await mine.Client.PostAsJsonAsync(
            "/api/guardians", new Guardians.CreateRequest("Portal", "Veli", "05339000001")));
        var link = await mine.Client.PostAsJsonAsync(
            $"/api/students/{student.StudentId}/guardians",
            new LinkGuardianToStudent.Request(guardian.Id, "anne", true));
        Assert.Equal(HttpStatusCode.Created, link.StatusCode);

        // Kendi öğrencisinin velisini düzenleyebilir
        var edit = await mine.Client.PatchAsJsonAsync(
            $"/api/guardians/{guardian.Id}", new Guardians.UpdateRequest("Portal", "Duzeltildi", "05339000001"));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        // Başka öğretmen aynı veliye dokunamaz
        var foreignEdit = await other.Client.PatchAsJsonAsync(
            $"/api/guardians/{guardian.Id}", new Guardians.UpdateRequest("Ele", "Gecirdim", "05339000001"));
        Assert.Equal(HttpStatusCode.Forbidden, foreignEdit.StatusCode);

        // Okul geneli veli rehberi hâlâ yalnızca yöneticide
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.Client.GetAsync("/api/guardians")).StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_delete_directly_and_must_open_a_request()
    {
        var admin = await CreateAdminClientAsync();
        var teacher = await SeedTeacherAsync(admin, "talep");
        var student = await ReadAsync<Teachers.TeacherStudentResponse>(await AddStudentAsync(teacher, "Silinecek"));
        var studentId = student.StudentId;

        // Doğrudan silme öğretmene kapalı
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.DeleteAsync($"/api/students/{studentId}")).StatusCode);

        // Gerekçesiz talep reddedilir
        var noReason = await teacher.Client.PostAsJsonAsync(
            $"/api/students/{studentId}/deletion-requests", new StudentDeletionRequests.CreateRequest("  "));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var created = await ReadAsync<StudentDeletionRequests.RequestResponse>(await teacher.Client.PostAsJsonAsync(
            $"/api/students/{studentId}/deletion-requests",
            new StudentDeletionRequests.CreateRequest("Okuldan ayrıldı")));
        Assert.Equal(StudentDeletionRequestStatus.Pending, created.Status);

        // Aynı öğrenci için ikinci bekleyen talep açılamaz
        var duplicate = await teacher.Client.PostAsJsonAsync(
            $"/api/students/{studentId}/deletion-requests",
            new StudentDeletionRequests.CreateRequest("Tekrar"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Yöneticiye ekran içi bildirim düştü
        await using var db = await _factory.CreateDbContextAsync();
        Assert.True(await db.StaffNotifications.AnyAsync(notification => notification.ReferenceId == created.Id));

        // Öğretmen kendi talebinin durumunu görebilir
        var own = await ReadAsync<List<StudentDeletionRequests.RequestResponse>>(
            await teacher.Client.GetAsync("/api/student-deletion-requests"));
        Assert.Contains(own, item => item.Id == created.Id);
        // Etki dökümü karar verecek olana (yöneticiye) ait
        Assert.Null(own.Single(item => item.Id == created.Id).Impact);
    }

    [Fact]
    public async Task Admin_sees_the_impact_and_approving_removes_the_student()
    {
        var admin = await CreateAdminClientAsync();
        var teacher = await SeedTeacherAsync(admin, "onay");
        var student = await ReadAsync<Teachers.TeacherStudentResponse>(await AddStudentAsync(teacher, "Onayli"));
        var studentId = student.StudentId;

        var created = await ReadAsync<StudentDeletionRequests.RequestResponse>(await teacher.Client.PostAsJsonAsync(
            $"/api/students/{studentId}/deletion-requests",
            new StudentDeletionRequests.CreateRequest("Kayıt yanlış açıldı")));

        var pending = await ReadAsync<List<StudentDeletionRequests.RequestResponse>>(
            await admin.GetAsync("/api/student-deletion-requests?status=Pending"));
        var row = pending.Single(item => item.Id == created.Id);
        Assert.Equal("Onayli Ogrenci", row.StudentName);
        Assert.StartsWith("Portalonay", row.RequestedByName);
        Assert.NotNull(row.Impact);
        Assert.Equal(1, row.Impact!.Enrollments);

        var approved = await admin.PostAsJsonAsync(
            $"/api/student-deletion-requests/{created.Id}/approve",
            new StudentDeletionRequests.DecisionRequest("Uygun"));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        await using var db = await _factory.CreateDbContextAsync();
        Assert.False(await db.Students.AnyAsync(item => item.Id == studentId));
        // Talep satırı öğrenciyle birlikte gider; kararın kalıcı izi audit'te durur.
        Assert.False(await db.StudentDeletionRequests.AnyAsync(item => item.Id == created.Id));
        Assert.True(await db.AuditLogs.AnyAsync(log =>
            log.Action == "student.deletion_request_approved" && log.EntityId == studentId));
    }

    [Fact]
    public async Task Rejecting_keeps_the_student_and_closes_the_request()
    {
        var admin = await CreateAdminClientAsync();
        var teacher = await SeedTeacherAsync(admin, "ret");
        var student = await ReadAsync<Teachers.TeacherStudentResponse>(await AddStudentAsync(teacher, "Kalan"));

        var created = await ReadAsync<StudentDeletionRequests.RequestResponse>(await teacher.Client.PostAsJsonAsync(
            $"/api/students/{student.StudentId}/deletion-requests",
            new StudentDeletionRequests.CreateRequest("Yanlışlıkla")));

        var rejected = await ReadAsync<StudentDeletionRequests.RequestResponse>(await admin.PostAsJsonAsync(
            $"/api/student-deletion-requests/{created.Id}/reject",
            new StudentDeletionRequests.DecisionRequest("Öğrenci devam ediyor")));
        Assert.Equal(StudentDeletionRequestStatus.Rejected, rejected.Status);

        // Karara bağlanmış talep tekrar karara bağlanamaz
        var again = await admin.PostAsJsonAsync(
            $"/api/student-deletion-requests/{created.Id}/approve",
            new StudentDeletionRequests.DecisionRequest(null));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        await using var db = await _factory.CreateDbContextAsync();
        Assert.True(await db.Students.AnyAsync(item => item.Id == student.StudentId));

        // Reddedilen talep kapandığı için yeni bir talep açılabilir
        var reopened = await teacher.Client.PostAsJsonAsync(
            $"/api/students/{student.StudentId}/deletion-requests",
            new StudentDeletionRequests.CreateRequest("Bu kez gerçekten ayrıldı"));
        Assert.Equal(HttpStatusCode.Created, reopened.StatusCode);
    }

    [Fact]
    public async Task Teacher_cannot_decide_on_a_request()
    {
        var admin = await CreateAdminClientAsync();
        var teacher = await SeedTeacherAsync(admin, "karar");
        var student = await ReadAsync<Teachers.TeacherStudentResponse>(await AddStudentAsync(teacher, "Karar"));

        var created = await ReadAsync<StudentDeletionRequests.RequestResponse>(await teacher.Client.PostAsJsonAsync(
            $"/api/students/{student.StudentId}/deletion-requests",
            new StudentDeletionRequests.CreateRequest("Gerekçe")));

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.PostAsJsonAsync(
            $"/api/student-deletion-requests/{created.Id}/approve",
            new StudentDeletionRequests.DecisionRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.Client.PostAsJsonAsync(
            $"/api/student-deletion-requests/{created.Id}/reject",
            new StudentDeletionRequests.DecisionRequest(null))).StatusCode);
    }

    // --- K: öğretmen kendi ders programını kurar (docs/10-decisions.md K1/K2) -------------
    // Buradaki risk yine yetki sızıntısı: uygunluk ve ders serisi uçlarında "kendi" kontrolü
    // unutulursa bir öğretmen başka bir öğretmenin haftalık programını değiştirebilir.

    [Fact]
    public async Task Teacher_sets_its_own_availability_but_not_another_teachers()
    {
        var admin = await CreateAdminClientAsync();
        var mine = await SeedTeacherAsync(admin, "uygunluk");
        var other = await SeedTeacherAsync(admin, "uygunluk2");

        var created = await ReadAsync<TeacherAvailabilities.AvailabilityResponse>(
            await mine.Client.PostAsJsonAsync(
                $"/api/teachers/{mine.TeacherId}/availability",
                new TeacherAvailabilities.CreateRequest(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(19, 0))));
        Assert.Equal(DayOfWeek.Monday, created.DayOfWeek);

        // Başkasının uygunluğunu açmak
        var onBehalf = await mine.Client.PostAsJsonAsync(
            $"/api/teachers/{other.TeacherId}/availability",
            new TeacherAvailabilities.CreateRequest(DayOfWeek.Friday, new TimeOnly(9, 0), new TimeOnly(19, 0)));
        Assert.Equal(HttpStatusCode.Forbidden, onBehalf.StatusCode);

        // Başkasının uygunluğunu kapatmak
        var foreignDelete = await other.Client.DeleteAsync(
            $"/api/teachers/{mine.TeacherId}/availability/{created.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, foreignDelete.StatusCode);

        // Kendi penceresini kapatabilir
        var ownDelete = await mine.Client.DeleteAsync(
            $"/api/teachers/{mine.TeacherId}/availability/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, ownDelete.StatusCode);

        await using var db = await _factory.CreateDbContextAsync();
        Assert.False(await db.TeacherAvailabilities.AnyAsync(row => row.Id == created.Id));
    }

    [Fact]
    public async Task Teacher_enters_and_ends_its_own_lesson_schedule()
    {
        var admin = await CreateAdminClientAsync();
        var teacher = await SeedTeacherAsync(admin, "program");
        var student = await ReadAsync<Teachers.TeacherStudentResponse>(await AddStudentAsync(teacher, "Programli"));

        var created = await ReadAsync<LessonSeriesFeatures.CreateResponse>(await teacher.Client.PostAsJsonAsync(
            "/api/lesson-series",
            new LessonSeriesFeatures.CreateRequest(
                student.EnrollmentId, DayOfWeek.Wednesday, new TimeOnly(17, 0), 45, new DateOnly(2026, 9, 1), null)));
        Assert.True(created.Generation.Created > 0);

        await using (var db = await _factory.CreateDbContextAsync())
        {
            Assert.Equal(created.Generation.Created, await db.Lessons.CountAsync(l => l.LessonSeriesId == created.Series.Id));
            // Takvimi değiştiren işlem audit'e düşer ve aktör admin değil, öğretmenin kendisi.
            var teacherUserId = await db.Teachers.Where(t => t.Id == teacher.TeacherId).Select(t => t.UserId).SingleAsync();
            Assert.True(await db.AuditLogs.AnyAsync(log =>
                log.Action == "lesson_series.created" && log.EntityId == created.Series.Id && log.ActorUserId == teacherUserId));
        }

        // Kendi serisini bitirebilir (durum değişikliği, silme değil) - hatalı girilen bir
        // programı düzeltmek için yönetici beklemesi gerekmez.
        var ended = await ReadAsync<LessonSeriesFeatures.LessonSeriesResponse>(await teacher.Client.PatchAsJsonAsync(
            $"/api/lesson-series/{created.Series.Id}", new LessonSeriesFeatures.EndRequest(new DateOnly(2026, 9, 2))));
        Assert.Equal(Abdera.Api.Modules.Scheduling.Domain.LessonSeriesStatus.Ended, ended.Status);

        await using (var db = await _factory.CreateDbContextAsync())
        {
            Assert.True(await db.AuditLogs.AnyAsync(log =>
                log.Action == "lesson_series.ended" && log.EntityId == created.Series.Id));
        }
    }

    [Fact]
    public async Task Teacher_cannot_touch_another_teachers_lesson_series()
    {
        var admin = await CreateAdminClientAsync();
        var mine = await SeedTeacherAsync(admin, "seri");
        var other = await SeedTeacherAsync(admin, "seri2");

        var otherStudent = await ReadAsync<Teachers.TeacherStudentResponse>(await AddStudentAsync(other, "Otekinin"));
        var otherSeries = await ReadAsync<LessonSeriesFeatures.CreateResponse>(await other.Client.PostAsJsonAsync(
            "/api/lesson-series",
            new LessonSeriesFeatures.CreateRequest(
                otherStudent.EnrollmentId, DayOfWeek.Thursday, new TimeOnly(16, 0), 45, new DateOnly(2026, 9, 1), null)));

        // Başkasının kaydı üzerinden seri açmak
        var hijack = await mine.Client.PostAsJsonAsync(
            "/api/lesson-series",
            new LessonSeriesFeatures.CreateRequest(
                otherStudent.EnrollmentId, DayOfWeek.Friday, new TimeOnly(16, 0), 45, new DateOnly(2026, 9, 1), null));
        Assert.Equal(HttpStatusCode.Forbidden, hijack.StatusCode);

        // Başkasının serisini yeniden üretmek, taşımak ya da bitirmek
        Assert.Equal(HttpStatusCode.Forbidden,
            (await mine.Client.PostAsync($"/api/lesson-series/{otherSeries.Series.Id}/generate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await mine.Client.PostAsJsonAsync(
                $"/api/lesson-series/{otherSeries.Series.Id}/reschedule",
                new LessonSeriesFeatures.RescheduleRequest(DayOfWeek.Monday, new TimeOnly(15, 0), 45, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await mine.Client.PatchAsJsonAsync(
                $"/api/lesson-series/{otherSeries.Series.Id}",
                new LessonSeriesFeatures.EndRequest(new DateOnly(2026, 9, 2)))).StatusCode);

        // Öğrencinin program listesi de kapsamlıdır: başka öğretmenin öğrencisinin programı
        // boş döner (404 değil - öğrencinin varlığı da sızdırılmaz, yalnızca kendi satırları).
        var foreignSchedule = await ReadAsync<List<LessonSeriesFeatures.StudentSeriesResponse>>(
            await mine.Client.GetAsync($"/api/students/{otherStudent.StudentId}/lesson-series"));
        Assert.Empty(foreignSchedule);

        var ownerSchedule = await ReadAsync<List<LessonSeriesFeatures.StudentSeriesResponse>>(
            await other.Client.GetAsync($"/api/students/{otherStudent.StudentId}/lesson-series"));
        Assert.Single(ownerSchedule);

        await using var db = await _factory.CreateDbContextAsync();
        Assert.Equal(
            Abdera.Api.Modules.Scheduling.Domain.LessonSeriesStatus.Active,
            await db.LessonSeries.Where(s => s.Id == otherSeries.Series.Id).Select(s => s.Status).SingleAsync());
    }
}
