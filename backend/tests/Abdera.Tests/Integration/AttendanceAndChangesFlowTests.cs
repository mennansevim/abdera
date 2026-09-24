using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Attendance.Features;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Modules.Scheduling.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
            new Abdera.Api.Modules.Progress.Features.LessonNotes.CreateRequest(
                "gam çalışması",
                "iyi",
                "günde 15 dk",
                "hızlanma",
                "Bach · Minuet in G",
                4));
        Assert.Equal(HttpStatusCode.Created, noteResponse.StatusCode);

        var progressResponse = await teacherClient.GetAsync($"/api/students/{seeded.StudentId}/progress");
        Assert.Equal(HttpStatusCode.OK, progressResponse.StatusCode);
        var progress = (await progressResponse.Content.ReadFromJsonAsync<Abdera.Api.Modules.Progress.Features.StudentProgress.ProgressResponse>(TestJson.Options))!;
        Assert.Equal(1, progress.EntryCount);
        Assert.Equal("Bach · Minuet in G", progress.Entries.Single().PieceTitle);
        Assert.Equal(4, progress.Entries.Single().PieceDifficulty);
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

    // Kullanıcı isteği: "takvimde ders taşındığında ilgili öğretmenin ekranına bildirim
    // gitsin." Taşıma yöneticinin sürükle-bırakıyla (talep + onay) yapılır; öğretmen
    // uygulamayı açtığında değişikliği zilinde görmeli.
    [Fact]
    public async Task Approved_change_request_creates_in_app_notification_for_the_lessons_teacher()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "notify1");

        var originalLesson = await db.Lessons.AsNoTracking().SingleAsync(lesson => lesson.Id == seeded.LessonId);
        var proposedStart = originalLesson.StartAt.AddDays(1);
        var createResponse = await admin.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/change-requests",
            new ChangeRequests.CreateRequest(null, proposedStart, proposedStart.AddMinutes(45)));
        var changeRequest = (await createResponse.Content.ReadFromJsonAsync<ChangeRequests.ChangeRequestResponse>(TestJson.Options))!;
        var approveResponse = await admin.PostAsync($"/api/change-requests/{changeRequest.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        var approved = (await approveResponse.Content.ReadFromJsonAsync<ChangeRequests.ApproveResponse>(TestJson.Options))!;

        using var teacherClient = _factory.CreateClient();
        await teacherClient.PostAsJsonAsync("/api/auth/login", new Login.Request(seeded.TeacherEmail, seeded.TeacherTempPassword));

        var listResponse = await teacherClient.GetAsync("/api/me/notifications");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = (await listResponse.Content.ReadFromJsonAsync<StaffNotifications.ListResponse>(TestJson.Options))!;

        var notification = Assert.Single(list.Items);
        Assert.Equal(StaffNotificationType.LessonMoved, notification.Type);
        Assert.Equal("lesson", notification.ReferenceType);
        // Referans YENİ ders satırını göstermeli - öğretmenin programında artık o var.
        Assert.Equal(approved.NewLessonId, notification.ReferenceId);
        Assert.Contains("→", notification.Body);
        Assert.Null(notification.ReadAt);
        Assert.Equal(1, list.UnreadCount);

        var readResponse = await teacherClient.PostAsync($"/api/me/notifications/{notification.Id}/read", null);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        var afterRead = (await (await teacherClient.GetAsync("/api/me/notifications"))
            .Content.ReadFromJsonAsync<StaffNotifications.ListResponse>(TestJson.Options))!;
        Assert.Equal(0, afterRead.UnreadCount);
        Assert.NotNull(afterRead.Items.Single().ReadAt);
    }

    // docs/04-permissions.md: hedef kaynak oturumdan çözümlenir. Bir öğretmen başkasının
    // bildirimini ne listeleyebilir ne de okundu işaretleyebilir.
    [Fact]
    public async Task Teacher_cannot_see_or_read_another_teachers_notification()
    {
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "notify2");

        await using var db = await _factory.CreateDbContextAsync();
        var originalLesson = await db.Lessons.AsNoTracking().SingleAsync(lesson => lesson.Id == seeded.LessonId);
        var proposedStart = originalLesson.StartAt.AddDays(1);
        var changeRequest = (await (await admin.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/change-requests",
                new ChangeRequests.CreateRequest(null, proposedStart, proposedStart.AddMinutes(45))))
            .Content.ReadFromJsonAsync<ChangeRequests.ChangeRequestResponse>(TestJson.Options))!;
        await admin.PostAsync($"/api/change-requests/{changeRequest.Id}/approve", null);

        using var ownerClient = _factory.CreateClient();
        await ownerClient.PostAsJsonAsync("/api/auth/login", new Login.Request(seeded.TeacherEmail, seeded.TeacherTempPassword));
        var owned = (await (await ownerClient.GetAsync("/api/me/notifications"))
            .Content.ReadFromJsonAsync<StaffNotifications.ListResponse>(TestJson.Options))!;
        var notificationId = owned.Items.Single().Id;

        var instruments = await (await admin.GetAsync("/api/instruments"))
            .Content.ReadFromJsonAsync<List<Instruments.InstrumentResponse>>(TestJson.Options);
        var guitar = instruments!.Single(instrument => instrument.Code == "GUITAR");
        const string otherEmail = "teacher-notify2-other@test.local";
        var other = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Diğer", "Öğretmen", [guitar.Id], otherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;

        using var otherClient = _factory.CreateClient();
        await otherClient.PostAsJsonAsync("/api/auth/login", new Login.Request(otherEmail, other.TemporaryPassword!));

        var otherList = (await (await otherClient.GetAsync("/api/me/notifications"))
            .Content.ReadFromJsonAsync<StaffNotifications.ListResponse>(TestJson.Options))!;
        Assert.Empty(otherList.Items);
        Assert.Equal(0, otherList.UnreadCount);

        var stealResponse = await otherClient.PostAsync($"/api/me/notifications/{notificationId}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, stealResponse.StatusCode);
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
    public async Task Admin_edits_lesson_detail_with_conflict_check_persistence_notification_and_audit()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "detail-edit");
        var original = await db.Lessons.AsNoTracking().SingleAsync(item => item.Id == seeded.LessonId);
        var newStart = original.StartAt.AddDays(2).AddMinutes(30);

        var response = await admin.PatchAsJsonAsync(
            $"/api/lessons/{seeded.LessonId}",
            new UpdateLesson.Request(
                seeded.StudentId,
                seeded.TeacherId,
                newStart,
                60,
                LessonStatus.Normal));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<UpdateLesson.Response>(TestJson.Options))!;

        db.ChangeTracker.Clear();
        Assert.Equal(LessonStatus.Rescheduled, (await db.Lessons.SingleAsync(item => item.Id == seeded.LessonId)).Status);
        var edited = await db.Lessons.SingleAsync(item => item.Id == result.LessonId);
        Assert.Equal(newStart, edited.StartAt);
        Assert.Equal(newStart.AddMinutes(60), edited.EndAt);
        Assert.Equal(seeded.StudentId, edited.StudentId);
        Assert.True(await db.AuditLogs.AnyAsync(item => item.Action == "lesson.updated" && item.EntityId == edited.Id));
        Assert.True(await db.NotificationJobs.AnyAsync(item =>
            item.ReferenceType == "lesson" && item.ReferenceId == edited.Id &&
            item.Type == Abdera.Api.Modules.Messaging.Domain.NotificationJobType.LessonReminder));

        // Ders detayından yapılan saat değişikliği de talep/onay akışıyla aynı şekilde
        // öğretmenin ekran içi ziline düşmeli; yalnızca veli WhatsApp işi açılması yetmez.
        var teacherUserId = await db.Teachers
            .Where(item => item.Id == seeded.TeacherId)
            .Select(item => item.UserId)
            .SingleAsync();
        var teacherNotice = await db.StaffNotifications.SingleOrDefaultAsync(item =>
            item.UserId == teacherUserId &&
            item.Type == StaffNotificationType.LessonMoved &&
            item.ReferenceType == "lesson" &&
            item.ReferenceId == edited.Id);
        Assert.NotNull(teacherNotice);
        Assert.Contains("→", teacherNotice.Body);
    }

    // Kullanıcı kuralı: "admin takvimdeki dersleri iptal etme, telafi tanımlama, güncelleme
    // yetkisine sahiptir." Saati geçmiş bir ders de buna dahil - yanlış girilmiş bir kaydı
    // düzeltmenin başka yolu yok ve silmek audit izini bozar. Öğretmende kural sürüyor.
    [Fact]
    public async Task Admin_can_move_a_lesson_that_already_started_but_a_teacher_cannot()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "past-edit");

        // PostgreSQL timestamptz MİKROSANİYE hassasiyetinde saklar, DateTimeOffset ise 100ns
        // (tick). Ham UtcNow'u gönderip geri okunanla birebir karşılaştırmak, kalan 8 tick
        // yüzünden CI'da kırılıyordu (bkz. db78091 - aynı sınıf hata ProgressFlowTests'te de
        // yaşanmıştı). Değeri veritabanının hassasiyetine indirerek gönderiyoruz; böylece
        // round-trip'i toleransla değil, TAM eşitlikle doğrulayabiliyoruz.
        var correctedStart = ToDatabasePrecision(DateTimeOffset.UtcNow.AddDays(-3).AddHours(1));

        var adminResponse = await admin.PatchAsJsonAsync(
            $"/api/lessons/{seeded.LessonId}",
            new UpdateLesson.Request(seeded.StudentId, seeded.TeacherId, correctedStart, 45, LessonStatus.Normal));
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        var corrected = await ReadUpdatedLessonAsync(db, adminResponse);
        Assert.Equal(correctedStart, corrected.StartAt);

        // Aynı istek öğretmende reddedilmeli.
        var teacherOwned = await SeedLessonAsync(admin, "past-edit-teacher");
        using var teacher = _factory.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login",
            new Login.Request(teacherOwned.TeacherEmail, teacherOwned.TeacherTempPassword))).EnsureSuccessStatusCode();

        var teacherResponse = await teacher.PatchAsJsonAsync(
            $"/api/lessons/{teacherOwned.LessonId}",
            new UpdateLesson.Request(teacherOwned.StudentId, teacherOwned.TeacherId, correctedStart, 45, LessonStatus.Normal));
        Assert.Equal(HttpStatusCode.BadRequest, teacherResponse.StatusCode);
    }

    // TimeSpan.TicksPerMicrosecond = 10 - kalan tick'leri atarak PostgreSQL'in sakladığı
    // değerin aynısını üretiriz.
    private static DateTimeOffset ToDatabasePrecision(DateTimeOffset value) =>
        new(value.Ticks - (value.Ticks % TimeSpan.TicksPerMicrosecond), value.Offset);

    private static async Task<Abdera.Api.Modules.Scheduling.Domain.Lesson> ReadUpdatedLessonAsync(
        Abdera.Api.Shared.AbderaDbContext db, HttpResponseMessage response)
    {
        var result = (await response.Content.ReadFromJsonAsync<UpdateLesson.Response>(TestJson.Options))!;
        db.ChangeTracker.Clear();
        return await db.Lessons.AsNoTracking().SingleAsync(item => item.Id == result.LessonId);
    }

    [Fact]
    public async Task Teacher_edits_own_lesson_occurrence_but_cannot_edit_another_teachers_lesson()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var own = await SeedLessonAsync(admin, "teacher-detail-own");
        var foreign = await SeedLessonAsync(admin, "teacher-detail-foreign");

        using var teacher = _factory.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new Login.Request(own.TeacherEmail, own.TeacherTempPassword)))
            .EnsureSuccessStatusCode();

        var original = await db.Lessons.AsNoTracking().SingleAsync(item => item.Id == own.LessonId);
        var movedStart = original.StartAt.AddDays(1).AddMinutes(15);
        var ownResponse = await teacher.PatchAsJsonAsync(
            $"/api/lessons/{own.LessonId}",
            new UpdateLesson.Request(own.StudentId, own.TeacherId, movedStart, 45, LessonStatus.Normal));
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);

        var foreignLesson = await db.Lessons.AsNoTracking().SingleAsync(item => item.Id == foreign.LessonId);
        var foreignResponse = await teacher.PatchAsJsonAsync(
            $"/api/lessons/{foreign.LessonId}",
            new UpdateLesson.Request(foreign.StudentId, foreign.TeacherId, foreignLesson.StartAt.AddDays(1), 45, LessonStatus.Normal));
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
    }

    [Fact]
    public async Task Teacher_cancels_own_lesson_with_makeup_but_cannot_cancel_another_teachers_lesson()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var own = await SeedLessonAsync(admin, "teacher-cancel-own");
        var foreign = await SeedLessonAsync(admin, "teacher-cancel-foreign");

        using var teacher = _factory.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new Login.Request(own.TeacherEmail, own.TeacherTempPassword)))
            .EnsureSuccessStatusCode();

        var ownResponse = await teacher.PostAsJsonAsync(
            $"/api/lessons/{own.LessonId}/cancel",
            new CancelLesson.Request(CancelLesson.CancelledBy.School, "Telafi hakkıyla iptal"));
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        var result = (await ownResponse.Content.ReadFromJsonAsync<CancelLesson.Response>(TestJson.Options))!;
        Assert.True(result.MakeupCreditEarned);
        Assert.True(await db.MakeupCredits.AnyAsync(item => item.SourceLessonId == own.LessonId));

        var ownCredits = await (await teacher.GetAsync($"/api/students/{own.StudentId}/makeup-credits"))
            .Content.ReadFromJsonAsync<List<MakeupCredits.CreditResponse>>(TestJson.Options);
        var ownCredit = ownCredits!.Single(item => item.SourceLessonId == own.LessonId);
        var pianoId = await GetPianoIdAsync(admin);
        var makeupStart = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(30).AddHours(11), TimeSpan.Zero);

        var useAsAnotherTeacher = await teacher.PostAsJsonAsync(
            $"/api/makeup-credits/{ownCredit.Id}/use",
            new MakeupCredits.UseRequest(foreign.TeacherId, pianoId, makeupStart, 45));
        Assert.Equal(HttpStatusCode.Forbidden, useAsAnotherTeacher.StatusCode);

        var useOwnCredit = await teacher.PostAsJsonAsync(
            $"/api/makeup-credits/{ownCredit.Id}/use",
            new MakeupCredits.UseRequest(own.TeacherId, pianoId, makeupStart, 45));
        Assert.Equal(HttpStatusCode.OK, useOwnCredit.StatusCode);
        var usedCredit = await db.MakeupCredits.AsNoTracking().SingleAsync(item => item.Id == ownCredit.Id);
        Assert.Equal(MakeupCreditStatus.Used, usedCredit.Status);
        Assert.NotNull(usedCredit.UsedLessonId);

        // Kullanıcı isteği: "ders telafi / iptal durumlarında bildirim gelsin". Öğretmen kendi
        // dersini iptal edip telafi planladı: yönetici ikisini de ziline alır, işlemi yapan
        // öğretmenin kendisine bildirim düşmez.
        var adminUserId = await db.Users.Where(user => user.Email == "admin@test.local").Select(user => user.Id).SingleAsync();
        var teacherUserId = await db.Teachers.Where(item => item.Id == own.TeacherId).Select(item => item.UserId).SingleAsync();
        var cancelNotice = await db.StaffNotifications.AsNoTracking().SingleAsync(item =>
            item.UserId == adminUserId && item.Type == StaffNotificationType.LessonCancelled && item.ReferenceId == own.LessonId);
        Assert.Contains("telafi hakkı tanındı", cancelNotice.Body);
        Assert.Contains("Telafi hakkıyla iptal", cancelNotice.Body);
        Assert.True(await db.StaffNotifications.AnyAsync(item =>
            item.UserId == adminUserId && item.Type == StaffNotificationType.MakeupScheduled && item.ReferenceId == usedCredit.UsedLessonId));
        Assert.False(await db.StaffNotifications.AnyAsync(item =>
            item.UserId == teacherUserId && (item.ReferenceId == own.LessonId || item.ReferenceId == usedCredit.UsedLessonId)));

        var foreignResponse = await teacher.PostAsJsonAsync(
            $"/api/lessons/{foreign.LessonId}/cancel",
            new CancelLesson.Request(CancelLesson.CancelledBy.School, "Yetkisiz deneme"));
        Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
    }

    [Fact]
    public async Task Admin_lesson_detail_edit_rejects_invalid_duration_and_conflicting_slot()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var first = await SeedLessonAsync(admin, "detail-conflict-a");
        var second = await SeedLessonAsync(admin, "detail-conflict-b");
        var firstLesson = await db.Lessons.AsNoTracking().SingleAsync(item => item.Id == first.LessonId);

        var invalid = await admin.PatchAsJsonAsync(
            $"/api/lessons/{second.LessonId}",
            new UpdateLesson.Request(second.StudentId, second.TeacherId, firstLesson.StartAt.AddDays(2), 0, LessonStatus.Normal));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        // Aynı öğretmenin başka bir öğrencisi için aktif kayıt açıp doğrudan ilk dersin
        // aralığına taşımayı deneriz; UI gizlese bile API bu çakışmayı reddetmelidir.
        var pianoId = await GetPianoIdAsync(admin);
        var enrollment = await admin.PostAsJsonAsync(
            $"/api/students/{second.StudentId}/enrollments",
            new Enrollments.CreateRequest(first.TeacherId, pianoId, new DateOnly(2026, 8, 1)));
        Assert.Equal(HttpStatusCode.Created, enrollment.StatusCode);

        var conflict = await admin.PatchAsJsonAsync(
            $"/api/lessons/{second.LessonId}",
            new UpdateLesson.Request(second.StudentId, first.TeacherId, firstLesson.StartAt, 45, LessonStatus.Normal));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task Cancelling_at_least_24_hours_before_earns_makeup_credit_and_credit_can_be_used()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "cxl1");

        // Serinin ilk dersi ertesi gün ve 24 saatten yakın olabilir; kuralı takvim gününe
        // bağımlı/flaky yapmamak için aynı seride garantili biçimde daha ilerideki dersi seç.
        var lesson = await db.Lessons.AsNoTracking()
            .Where(item => item.StudentId == seeded.StudentId && item.StartAt >= DateTimeOffset.UtcNow.AddHours(24))
            .OrderBy(item => item.StartAt)
            .FirstAsync();
        Assert.True((lesson.StartAt - DateTimeOffset.UtcNow).TotalHours >= 24);

        var cancelResponse = await admin.PostAsJsonAsync($"/api/lessons/{lesson.Id}/cancel",
            new CancelLesson.Request(CancelLesson.CancelledBy.Guardian, "hasta"));
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelResult = (await cancelResponse.Content.ReadFromJsonAsync<CancelLesson.Response>(TestJson.Options))!;
        Assert.True(cancelResult.MakeupCreditEarned);

        var credits = await (await admin.GetAsync($"/api/students/{seeded.StudentId}/makeup-credits"))
            .Content.ReadFromJsonAsync<List<MakeupCredits.CreditResponse>>(TestJson.Options);
        var credit = credits!.Single();
        Assert.Equal(MakeupCreditStatus.Available, credit.Status);
        Assert.Equal(MakeupCreditEarnedReason.GuardianCancelled24H, credit.EarnedReason);

        var clock = _factory.Services.GetRequiredService<IClock>();
        var sourceLessonDate = DateOnly.FromDateTime(clock.ToSchoolLocal(lesson.StartAt).Date);
        var sameDayStart = LessonGenerator.ToUtcInstant(sourceLessonDate, new TimeOnly(20, 0), clock.SchoolTimeZone);
        var invalidUseResponse = await admin.PostAsJsonAsync($"/api/makeup-credits/{credit.Id}/use",
            new MakeupCredits.UseRequest(seeded.TeacherId, (await GetPianoIdAsync(admin)), sameDayStart, 45));
        Assert.Equal(HttpStatusCode.BadRequest, invalidUseResponse.StatusCode);

        var makeupStart = LessonGenerator.ToUtcInstant(sourceLessonDate.AddDays(1), new TimeOnly(11, 0), clock.SchoolTimeZone);
        var useResponse = await admin.PostAsJsonAsync($"/api/makeup-credits/{credit.Id}/use",
            new MakeupCredits.UseRequest(seeded.TeacherId, (await GetPianoIdAsync(admin)), makeupStart, 45));
        Assert.Equal(HttpStatusCode.OK, useResponse.StatusCode);

        var repeatedUseResponse = await admin.PostAsJsonAsync($"/api/makeup-credits/{credit.Id}/use",
            new MakeupCredits.UseRequest(seeded.TeacherId, (await GetPianoIdAsync(admin)), makeupStart.AddDays(1), 45));
        Assert.Equal(HttpStatusCode.Conflict, repeatedUseResponse.StatusCode);

        var usedCredit = await db.MakeupCredits.AsNoTracking().SingleAsync(c => c.Id == credit.Id);
        Assert.Equal(MakeupCreditStatus.Used, usedCredit.Status);
        Assert.NotNull(usedCredit.UsedLessonId);

        var makeupLesson = await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == usedCredit.UsedLessonId);
        Assert.Equal(LessonStatus.Makeup, makeupLesson.Status);
        Assert.Null(makeupLesson.LessonSeriesId);
        Assert.Equal(sourceLessonDate.AddDays(1), DateOnly.FromDateTime(clock.ToSchoolLocal(makeupLesson.StartAt).Date));
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

    // Takvimdeki "Telafisiz iptal": okul kaynaklı iptal normalde her zaman kredi doğurur,
    // ama tatil / yanlış açılmış ders gibi durumlarda kullanıcı açıkça telafisiz iptal
    // edebilmeli. Açık seçim politikanın önüne geçer.
    [Fact]
    public async Task Cancelling_without_a_makeup_credit_creates_no_credit_even_when_the_school_cancels()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "cxl4");

        var cancelResponse = await admin.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/cancel",
            new CancelLesson.Request(CancelLesson.CancelledBy.School, "tatil", GrantMakeupCredit: false));
        var result = (await cancelResponse.Content.ReadFromJsonAsync<CancelLesson.Response>(TestJson.Options))!;

        Assert.False(result.MakeupCreditEarned);
        Assert.Empty(await db.MakeupCredits.Where(c => c.SourceLessonId == seeded.LessonId).ToListAsync());

        var cancelled = await db.Lessons.AsNoTracking().SingleAsync(l => l.Id == seeded.LessonId);
        Assert.Equal(LessonStatus.Cancelled, cancelled.Status);

        // Karar audit'e düşmeli: "bu öğrenciye telafi neden verilmedi" sorusunun tek kaynağı bu.
        var audit = await db.AuditLogs.AsNoTracking()
            .Where(log => log.EntityId == seeded.LessonId && log.Action == "lesson.cancelled")
            .SingleAsync();
        using var after = JsonDocument.Parse(audit.AfterJson!);
        Assert.False(after.RootElement.GetProperty("MakeupCreditEarned").GetBoolean());
        Assert.True(after.RootElement.GetProperty("PolicyMakeupCredit").GetBoolean());
        Assert.True(after.RootElement.GetProperty("MakeupCreditOverridden").GetBoolean());
    }

    // Açık seçim ters yönde de çalışmalı: politikanın (24 saatten az kala veli iptali)
    // kendiliğinden vermeyeceği bir kredi, kullanıcı isterse yine de tanınır.
    [Fact]
    public async Task Explicitly_granting_a_makeup_credit_overrides_the_24_hour_rule()
    {
        await using var db = await _factory.CreateDbContextAsync();
        var admin = await CreateAdminClientAsync();
        var seeded = await SeedLessonAsync(admin, "cxl5");

        var nearStart = DateTimeOffset.UtcNow.AddHours(2);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE lessons SET start_at = {nearStart}, end_at = {nearStart.AddMinutes(45)} WHERE id = {seeded.LessonId}");

        var cancelResponse = await admin.PostAsJsonAsync($"/api/lessons/{seeded.LessonId}/cancel",
            new CancelLesson.Request(CancelLesson.CancelledBy.Guardian, "öğrenci hastalandı", GrantMakeupCredit: true));
        var result = (await cancelResponse.Content.ReadFromJsonAsync<CancelLesson.Response>(TestJson.Options))!;

        Assert.True(result.MakeupCreditEarned);
        Assert.Single(await db.MakeupCredits.Where(c => c.SourceLessonId == seeded.LessonId).ToListAsync());
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
