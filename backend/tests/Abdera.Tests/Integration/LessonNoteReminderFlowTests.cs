using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Attendance.Features;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Progress.Features;
using Abdera.Api.Modules.Progress.Infrastructure;
using Abdera.Api.Modules.Scheduling.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Abdera.Tests.Integration;

// Kullanıcı isteği: "öğretmenlere tamamlanan dersler ile ilgili yorum girmelerini
// hatırlatmalara ekleyelim eğer girmedilerse." Uçtan uca: yoklaması GELDİ girilmiş ama notu
// yazılmamış ders öğretmenin listesine ve zilindeki tek hatırlatmaya düşer; aynı gün ve ertesi
// gün okunmuş hatırlatma yeniden açılmaz, üç gün sonra açılır ("her gün olmasın, 3 günde bir");
// not yazılınca hatırlatma kapanır.
public class LessonNoteReminderFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private const string CronPath = "/api/internal/cron/lesson-note-reminders";
    private const string Secret = "test-cron-secret";
    private readonly AbderaWebApplicationFactory _factory;

    public LessonNoteReminderFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> WithCronSecret() =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["CRON_SECRET"] = Secret })));

    private record SeededLessons(Guid TeacherId, string TeacherEmail, string TeacherPassword, Guid PresentLessonId, Guid AbsentLessonId);

    // Öğretmen (giriş hesaplı) -> öğrenci -> kayıt -> seri; üretilen ilk iki ders geçmişe
    // taşınır, çünkü yalnızca BİTMİŞ ders yorum bekler.
    private async Task<SeededLessons> SeedPastLessonsAsync(HttpClient admin)
    {
        var instruments = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>("/api/instruments", TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");

        var teacherEmail = "note-reminder-teacher@test.local";
        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Yorum", "Öğretmen", [piano.Id], teacherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;
        var student = (await (await admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Yorum", "Öğrenci", new DateOnly(2014, 1, 1))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                new Enrollments.CreateRequest(teacher.Teacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
        var series = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, DayOfWeek.Tuesday, new TimeOnly(16, 0), 45, DateOnly.FromDateTime(DateTime.UtcNow), null));
        Assert.Equal(HttpStatusCode.Created, series.StatusCode);

        var lessons = await admin.GetFromJsonAsync<List<Calendar.LessonResponse>>(
            $"/api/calendar?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}" +
            $"&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(60).ToString("O"))}&teacherId={teacher.Teacher.Id}",
            TestJson.Options);
        var firstTwo = lessons!.OrderBy(l => l.StartAt).Take(2).ToList();
        Assert.Equal(2, firstTwo.Count);

        await using var db = await _factory.CreateDbContextAsync();
        await MoveToPastAsync(db, firstTwo[0].Id, DateTimeOffset.UtcNow.AddDays(-2));
        await MoveToPastAsync(db, firstTwo[1].Id, DateTimeOffset.UtcNow.AddDays(-3));

        return new SeededLessons(teacher.Teacher.Id, teacherEmail, teacher.TemporaryPassword!, firstTwo[0].Id, firstTwo[1].Id);
    }

    private static async Task MoveToPastAsync(Abdera.Api.Shared.AbderaDbContext db, Guid lessonId, DateTimeOffset startAt) =>
        await db.Lessons.Where(l => l.Id == lessonId).ExecuteUpdateAsync(set => set
            .SetProperty(l => l.StartAt, startAt)
            .SetProperty(l => l.EndAt, startAt.AddMinutes(45)));

    [Fact]
    public async Task Completed_lesson_without_a_note_is_reminded_every_three_days_until_the_note_is_written()
    {
        var admin = _factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();
        var seeded = await SeedPastLessonsAsync(admin);

        using var teacher = _factory.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new Login.Request(seeded.TeacherEmail, seeded.TeacherPassword))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, (await teacher.PostAsJsonAsync($"/api/lessons/{seeded.PresentLessonId}/attendance",
            new MarkAttendance.MarkRequest(AttendanceStatus.Present, null))).StatusCode);
        // Gelmeyen öğrencinin dersinden yorum beklenmez.
        Assert.Equal(HttpStatusCode.Created, (await teacher.PostAsJsonAsync($"/api/lessons/{seeded.AbsentLessonId}/attendance",
            new MarkAttendance.MarkRequest(AttendanceStatus.Absent, null))).StatusCode);

        var pending = await teacher.GetFromJsonAsync<PendingLessonNotes.ListResponse>("/api/me/pending-lesson-notes", TestJson.Options);
        var item = Assert.Single(pending!.Items);
        Assert.Equal(seeded.PresentLessonId, item.LessonId);
        Assert.Equal("Yorum Öğrenci", item.StudentName);

        // Liste öğretmenin kendi çalışma listesi - yöneticide karşılığı yok.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/me/pending-lesson-notes")).StatusCode);

        using var cronFactory = WithCronSecret();
        using var cron = cronFactory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await cron.GetAsync(CronPath)).StatusCode);
        cron.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Secret);

        Assert.Equal(HttpStatusCode.OK, (await cron.GetAsync(CronPath)).StatusCode);
        var reminder = await SingleReminderAsync(teacher);
        Assert.Null(reminder.ReadAt);
        Assert.Equal("1 dersin yorumu bekliyor", reminder.Title);
        Assert.Contains("Yorum Öğrenci", reminder.Body);

        // Aynı gün ikinci tur: yeni satır açılmaz, okunmuş hatırlatma tekrar açılmaz.
        Assert.Equal(HttpStatusCode.OK, (await teacher.PostAsync($"/api/me/notifications/{reminder.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cron.GetAsync(CronPath)).StatusCode);
        var sameDay = await SingleReminderAsync(teacher);
        Assert.Equal(reminder.Id, sameDay.Id);
        Assert.NotNull(sameDay.ReadAt);

        // Ertesi gün: not hâlâ yok ama son dürtüşün üzerinden üç gün geçmedi -> okunmuş kalır.
        // UpdatedAt eski görünse bile belirleyici olan son öne çıkış (CreatedAt).
        await using (var db = await _factory.CreateDbContextAsync())
        {
            await db.StaffNotifications.Where(n => n.Id == reminder.Id).ExecuteUpdateAsync(set => set
                .SetProperty(n => n.UpdatedAt, DateTimeOffset.UtcNow.AddDays(-1))
                .SetProperty(n => n.CreatedAt, DateTimeOffset.UtcNow.AddDays(-1)));
        }
        Assert.Equal(HttpStatusCode.OK, (await cron.GetAsync(CronPath)).StatusCode);
        var nextDay = await SingleReminderAsync(teacher);
        Assert.Equal(reminder.Id, nextDay.Id);
        Assert.NotNull(nextDay.ReadAt);

        // Üç gün sonra: not hâlâ yok -> hatırlatma yeniden okunmamış.
        await using (var db = await _factory.CreateDbContextAsync())
        {
            await db.StaffNotifications.Where(n => n.Id == reminder.Id).ExecuteUpdateAsync(set => set
                .SetProperty(n => n.CreatedAt, DateTimeOffset.UtcNow.AddDays(-LessonNoteReminderJob.ResurfaceEveryDays)));
        }
        Assert.Equal(HttpStatusCode.OK, (await cron.GetAsync(CronPath)).StatusCode);
        var threeDaysLater = await SingleReminderAsync(teacher);
        Assert.Equal(reminder.Id, threeDaysLater.Id);
        Assert.Null(threeDaysLater.ReadAt);

        // Son eksik yazılınca liste boşalır ve hatırlatma turu beklemeden kapanır.
        Assert.Equal(HttpStatusCode.Created, (await teacher.PostAsJsonAsync($"/api/lessons/{seeded.PresentLessonId}/notes",
            new LessonNotes.CreateRequest("gam", "iyi gidiyor", null, null))).StatusCode);
        var afterNote = await teacher.GetFromJsonAsync<PendingLessonNotes.ListResponse>("/api/me/pending-lesson-notes", TestJson.Options);
        Assert.Empty(afterNote!.Items);
        Assert.NotNull((await SingleReminderAsync(teacher)).ReadAt);

        // Bir sonraki tur da onu yeniden açmaz (hatırlatılacak ders kalmadı).
        await using (var db = await _factory.CreateDbContextAsync())
        {
            await db.StaffNotifications.Where(n => n.Id == reminder.Id).ExecuteUpdateAsync(set => set
                .SetProperty(n => n.CreatedAt, DateTimeOffset.UtcNow.AddDays(-LessonNoteReminderJob.ResurfaceEveryDays)));
        }
        Assert.Equal(HttpStatusCode.OK, (await cron.GetAsync(CronPath)).StatusCode);
        Assert.NotNull((await SingleReminderAsync(teacher)).ReadAt);
    }

    private static async Task<StaffNotifications.StaffNotificationResponse> SingleReminderAsync(HttpClient teacher)
    {
        var list = await teacher.GetFromJsonAsync<StaffNotifications.ListResponse>("/api/me/notifications", TestJson.Options);
        var reminder = Assert.Single(list!.Items, n => n.Type == StaffNotificationType.LessonNoteMissing);
        Assert.Equal(LessonNoteReminderJob.ReferenceType, reminder.ReferenceType);
        return reminder;
    }
}
