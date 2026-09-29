using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Attendance.Features;
using Abdera.Api.Modules.Attendance.Infrastructure;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Progress.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Modules.Scheduling.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Tests.Integration;

// Kullanıcı isteği: "öğretmenlere bildirim olarak öğrencinin derse katılım sağlayıp
// sağlamadıklarını sor ... seçim yapılmayanlar bildirimlerde kalsın ... yorum gelişme notu
// girerse otomatik olarak geldi işaretle." Uçtan uca: bitmiş ve yoklaması girilmemiş her ders
// öğretmenin ziline ayrı bir soru düşer; soru okundu işaretlemekle kapanmaz, yoklama girilince
// ya da derse not yazılınca kapanır.
public class AttendanceReminderFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public AttendanceReminderFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private record SeededLessons(string TeacherEmail, string TeacherPassword, Guid AbsentLessonId, Guid NotedLessonId, Guid FutureLessonId);

    // Öğretmen (giriş hesaplı) -> öğrenci -> kayıt -> seri; üretilen ilk iki ders geçmişe
    // taşınır (yalnızca BİTMİŞ ders için sorulur), üçüncüsü gelecekte kalır.
    private async Task<SeededLessons> SeedAsync(HttpClient admin)
    {
        var instruments = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>("/api/instruments", TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        var teacherEmail = "attendance-reminder-teacher@test.local";
        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Yoklama", "Öğretmen", [piano.Id], teacherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Yoklama", "Öğrenci", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Teacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
        var series = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, DayOfWeek.Wednesday, new TimeOnly(16, 0), 45, DateOnly.FromDateTime(DateTime.UtcNow), null));
        Assert.Equal(HttpStatusCode.Created, series.StatusCode);

        var lessons = await admin.GetFromJsonAsync<List<Calendar.LessonResponse>>(
            $"/api/calendar?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}" +
            $"&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(60).ToString("O"))}&teacherId={teacher.Teacher.Id}",
            TestJson.Options);
        var firstThree = lessons!.OrderBy(l => l.StartAt).Take(3).ToList();
        Assert.Equal(3, firstThree.Count);

        await using var db = await _factory.CreateDbContextAsync();
        await MoveToPastAsync(db, firstThree[0].Id, DateTimeOffset.UtcNow.AddDays(-2));
        await MoveToPastAsync(db, firstThree[1].Id, DateTimeOffset.UtcNow.AddDays(-3));

        return new SeededLessons(teacherEmail, teacher.TemporaryPassword!, firstThree[0].Id, firstThree[1].Id, firstThree[2].Id);
    }

    private static async Task MoveToPastAsync(AbderaDbContext db, Guid lessonId, DateTimeOffset startAt) =>
        await db.Lessons.Where(l => l.Id == lessonId).ExecuteUpdateAsync(set => set
            .SetProperty(l => l.StartAt, startAt)
            .SetProperty(l => l.EndAt, startAt.AddMinutes(45)));

    private async Task<AttendanceReminderJob.Result> RunJobAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await AttendanceReminderJob.RunAsync(
            scope.ServiceProvider.GetRequiredService<AbderaDbContext>(),
            scope.ServiceProvider.GetRequiredService<IClock>(),
            scope.ServiceProvider.GetRequiredService<IStaffNotifier>());
    }

    private static async Task<List<StaffNotifications.StaffNotificationResponse>> QuestionsAsync(HttpClient client)
    {
        var list = await client.GetFromJsonAsync<StaffNotifications.ListResponse>("/api/me/notifications", TestJson.Options);
        return list!.Items.Where(n => n.Type == StaffNotificationType.AttendanceMissing).ToList();
    }

    [Fact]
    public async Task Finished_lesson_without_attendance_is_asked_until_attendance_or_a_note_answers_it()
    {
        var admin = _factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();
        var seeded = await SeedAsync(admin);

        using var teacher = _factory.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new Login.Request(seeded.TeacherEmail, seeded.TeacherPassword))).EnsureSuccessStatusCode();

        // Bitmiş iki ders için birer soru; gelecekteki ders için soru yok, yöneticiye soru yok.
        await RunJobAsync();
        var questions = await QuestionsAsync(teacher);
        Assert.Equal(2, questions.Count);
        Assert.All(questions, q => Assert.Null(q.ReadAt));
        Assert.All(questions, q => Assert.Equal(AttendanceReminderJob.ReferenceType, q.ReferenceType));
        Assert.Equal(
            new[] { seeded.AbsentLessonId, seeded.NotedLessonId }.OrderBy(id => id),
            questions.Select(q => q.ReferenceId).OrderBy(id => id));
        Assert.Contains("Yoklama Öğrenci", questions[0].Body);
        Assert.Empty(await QuestionsAsync(admin));

        // Invariant: soru okumakla kapanmaz - ne tek tek ne "tümünü okundu işaretle" ile.
        var first = questions[0];
        Assert.Equal(HttpStatusCode.OK, (await teacher.PostAsync($"/api/me/notifications/{first.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await teacher.PostAsync("/api/me/notifications/read-all", null)).StatusCode);
        Assert.All(await QuestionsAsync(teacher), q => Assert.Null(q.ReadAt));

        // Tur tekrarlanırsa aynı ders için ikinci soru açılmaz.
        await RunJobAsync();
        Assert.Equal(2, (await QuestionsAsync(teacher)).Count);

        // Zildeki "Gelmedi": yoklama girilir, soru kapanır.
        Assert.Equal(HttpStatusCode.Created, (await teacher.PostAsJsonAsync($"/api/lessons/{seeded.AbsentLessonId}/attendance",
            new MarkAttendance.MarkRequest(AttendanceStatus.Absent, null))).StatusCode);
        Assert.NotNull((await QuestionsAsync(teacher)).Single(q => q.ReferenceId == seeded.AbsentLessonId).ReadAt);

        // Derse not yazılırsa yoklama kendiliğinden "geldi" olur ve soru kapanır.
        Assert.Equal(HttpStatusCode.Created, (await teacher.PostAsJsonAsync($"/api/lessons/{seeded.NotedLessonId}/notes",
            new LessonNotes.CreateRequest("gam", "iyi gidiyor", null, null))).StatusCode);
        var autoAttendance = await teacher.GetFromJsonAsync<MarkAttendance.AttendanceResponse>(
            $"/api/lessons/{seeded.NotedLessonId}/attendance", TestJson.Options);
        Assert.Equal(AttendanceStatus.Present, autoAttendance!.Status);
        Assert.All(await QuestionsAsync(teacher), q => Assert.NotNull(q.ReadAt));

        await using (var db = await _factory.CreateDbContextAsync())
        {
            Assert.Equal(LessonStatus.Completed, (await db.Lessons.SingleAsync(l => l.Id == seeded.NotedLessonId)).Status);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "lesson.attendance_marked_from_note"));
        }

        // Invariant: henüz başlamamış derse yazılan not yoklama girmez.
        Assert.Equal(HttpStatusCode.Created, (await teacher.PostAsJsonAsync($"/api/lessons/{seeded.FutureLessonId}/notes",
            new LessonNotes.CreateRequest(null, "gelecek derse hazırlık", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await teacher.GetAsync($"/api/lessons/{seeded.FutureLessonId}/attendance")).StatusCode);

        // Cevaplanmış soru bir sonraki turda yeniden açılmaz.
        await RunJobAsync();
        Assert.All(await QuestionsAsync(teacher), q => Assert.NotNull(q.ReadAt));
    }

    [Fact]
    public async Task Question_of_a_lesson_cancelled_later_is_closed_by_the_next_run()
    {
        var admin = _factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();

        var instruments = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>("/api/instruments", TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");
        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("İptal", "Öğretmen", [piano.Id], "attendance-cancel-teacher@test.local")))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("İptal", "Öğrenci", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Teacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, DayOfWeek.Thursday, new TimeOnly(16, 0), 45, DateOnly.FromDateTime(DateTime.UtcNow), null))).StatusCode);

        Guid lessonId;
        await using (var db = await _factory.CreateDbContextAsync())
        {
            lessonId = await db.Lessons.Where(l => l.TeacherId == teacher.Teacher.Id).OrderBy(l => l.StartAt).Select(l => l.Id).FirstAsync();
            await MoveToPastAsync(db, lessonId, DateTimeOffset.UtcNow.AddDays(-1));
        }

        using var teacherClient = _factory.CreateClient();
        (await teacherClient.PostAsJsonAsync("/api/auth/login",
            new Login.Request("attendance-cancel-teacher@test.local", teacher.TemporaryPassword!))).EnsureSuccessStatusCode();

        await RunJobAsync();
        Assert.Null(Assert.Single(await QuestionsAsync(teacherClient)).ReadAt);

        // Ders sonradan iptal edildi (yanlış açılmış ders): soracak bir şey kalmadı.
        await using (var db = await _factory.CreateDbContextAsync())
        {
            await db.Lessons.Where(l => l.Id == lessonId)
                .ExecuteUpdateAsync(set => set.SetProperty(l => l.Status, LessonStatus.Cancelled));
        }
        var result = await RunJobAsync();
        Assert.True(result.ClearedCount >= 1);
        Assert.NotNull(Assert.Single(await QuestionsAsync(teacherClient)).ReadAt);
    }
}
