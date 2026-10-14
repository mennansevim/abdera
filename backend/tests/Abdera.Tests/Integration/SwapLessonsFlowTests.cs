using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Modules.Scheduling.Features;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Tests.Integration;

// Kullanıcı isteği: "Perşembe 15 Ferit'i Cuma 15'e, Cuma 15 Emir Ali'yi Perşembe 15'e alacağız."
// Tek tek taşıma karşı ders yüzünden çakışmaya takılıyordu; takas iki dersi birlikte taşır.
public class SwapLessonsFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private static readonly TimeSpan SchoolOffset = TimeSpan.FromHours(3);
    private readonly AbderaWebApplicationFactory _factory;

    public SwapLessonsFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private record Setup(
        HttpClient Admin, Guid TeacherId, string? TeacherPassword, Guid ThursdayStudentId, Guid FridayStudentId,
        Guid ThursdaySeriesId, Guid FridaySeriesId, Lesson ThursdayLesson, Lesson FridayLesson);

    // Aynı öğretmen: Perşembe 15:00 bir öğrenci, Cuma 15:00 diğeri. Aynı haftanın iki dersi döner.
    private async Task<Setup> ArrangeAsync(
        int thursdayDuration = 45, int fridayDuration = 45, string? teacherEmail = null)
    {
        var admin = _factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();

        var instruments = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>("/api/instruments", TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var teacherCreate = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Takas", $"Ogretmen{suffix}", [piano.Id], teacherEmail)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;
        var teacher = teacherCreate.Teacher;

        async Task<(Guid StudentId, Guid SeriesId)> StudentWithSeriesAsync(string name, DayOfWeek day, int duration)
        {
            var student = (await (await admin.PostAsJsonAsync("/api/students",
                    new Students.CreateRequest(name, suffix, new DateOnly(2015, 3, 3))))
                .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
            var phone = $"0555{Random.Shared.Next(1000000, 9999999)}";
            var guardian = (await (await admin.PostAsJsonAsync("/api/guardians",
                    new Guardians.CreateRequest($"Veli{name}", suffix, phone)))
                .Content.ReadFromJsonAsync<Guardians.GuardianResponse>(TestJson.Options))!;
            (await admin.PostAsJsonAsync($"/api/students/{student.Id}/guardians",
                new LinkGuardianToStudent.Request(guardian.Id, "anne", true))).EnsureSuccessStatusCode();
            // Rıza açık olmadan hatırlatma job'ı kurulmaz; takasın job'ları yenilediği görülsün.
            (await admin.PatchAsJsonAsync($"/api/guardians/{guardian.Id}",
                new Guardians.UpdateRequest(guardian.FirstName, guardian.LastName, phone, NotificationConsent: true)))
                .EnsureSuccessStatusCode();
            var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                    new Enrollments.CreateRequest(teacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
                .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
            var seriesResponse = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
                enrollment.Id, day, new TimeOnly(15, 0), duration, new DateOnly(2026, 8, 3), null));
            Assert.True(seriesResponse.StatusCode == HttpStatusCode.Created, await seriesResponse.Content.ReadAsStringAsync());
            var series = (await seriesResponse.Content.ReadFromJsonAsync<LessonSeriesFeatures.CreateResponse>(TestJson.Options))!;
            return (student.Id, series.Series.Id);
        }

        var thursday = await StudentWithSeriesAsync("Ferit", DayOfWeek.Thursday, thursdayDuration);
        var friday = await StudentWithSeriesAsync("EmirAli", DayOfWeek.Friday, fridayDuration);

        await using var db = await _factory.CreateDbContextAsync();
        var thursdayLessons = await db.Lessons.Where(l => l.LessonSeriesId == thursday.SeriesId).OrderBy(l => l.StartAt).ToListAsync();
        var fridayLessons = await db.Lessons.Where(l => l.LessonSeriesId == friday.SeriesId).ToListAsync();
        // Ertesi günü Cuma dersi olan ilk Perşembe - aynı haftanın ikilisi.
        var thursdayLesson = thursdayLessons.First(t => fridayLessons.Any(f => f.StartAt == t.StartAt.AddDays(1)));
        var fridayLesson = fridayLessons.Single(f => f.StartAt == thursdayLesson.StartAt.AddDays(1));

        return new Setup(admin, teacher.Id, teacherCreate.TemporaryPassword, thursday.StudentId, friday.StudentId,
            thursday.SeriesId, friday.SeriesId, thursdayLesson, fridayLesson);
    }

    [Fact]
    public async Task Swapping_once_moves_only_these_two_lessons_and_re_arms_reminders()
    {
        var setup = await ArrangeAsync();

        var response = await setup.Admin.PostAsJsonAsync($"/api/lessons/{setup.ThursdayLesson.Id}/swap",
            new SwapLessons.Request(setup.FridayLesson.Id, SwapLessons.SwapScope.Once));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var swapped = (await response.Content.ReadFromJsonAsync<SwapLessons.Response>(TestJson.Options))!;

        await using var db = await _factory.CreateDbContextAsync();
        var originals = await db.Lessons.Where(l => l.Id == setup.ThursdayLesson.Id || l.Id == setup.FridayLesson.Id).ToListAsync();
        Assert.All(originals, l => Assert.Equal(LessonStatus.Rescheduled, l.Status));

        var ferit = await db.Lessons.SingleAsync(l => l.Id == swapped.FirstId);
        var emir = await db.Lessons.SingleAsync(l => l.Id == swapped.SecondId);
        Assert.Equal(setup.ThursdayStudentId, ferit.StudentId);
        Assert.Equal(setup.FridayLesson.StartAt, ferit.StartAt);
        Assert.Equal(setup.FridayStudentId, emir.StudentId);
        Assert.Equal(setup.ThursdayLesson.StartAt, emir.StartAt);

        // Yalnız bu hafta: sonraki haftanın Perşembe dersi programında yerinde duruyor.
        Assert.True(await db.Lessons.AnyAsync(l =>
            l.LessonSeriesId == setup.ThursdaySeriesId && l.StartAt == setup.ThursdayLesson.StartAt.AddDays(7) &&
            l.Status == LessonStatus.Normal));
        Assert.Equal(LessonSeriesStatus.Active,
            await db.LessonSeries.Where(s => s.Id == setup.ThursdaySeriesId).Select(s => s.Status).SingleAsync());

        // Eski saatin hatırlatması iptal, yeni saate yenisi kuruldu (CLAUDE.md eski job iptali).
        Assert.False(await db.NotificationJobs.AnyAsync(j =>
            j.ReferenceType == "lesson" && j.ReferenceId == setup.ThursdayLesson.Id && j.Status == NotificationJobStatus.Pending));
        Assert.True(await db.NotificationJobs.AnyAsync(j =>
            j.Type == NotificationJobType.LessonReminder && j.ReferenceType == "lesson" && j.ReferenceId == ferit.Id &&
            j.Status == NotificationJobStatus.Pending));

        Assert.Equal(2, await db.AuditLogs.CountAsync(log =>
            log.Action == "lesson.swapped" && (log.EntityId == ferit.Id || log.EntityId == emir.Id)));
    }

    [Fact]
    public async Task Swapping_recurring_exchanges_the_weekly_schedules_from_that_week_on()
    {
        var setup = await ArrangeAsync();

        var response = await setup.Admin.PostAsJsonAsync($"/api/lessons/{setup.ThursdayLesson.Id}/swap",
            new SwapLessons.Request(setup.FridayLesson.Id, SwapLessons.SwapScope.Recurring));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var swapped = (await response.Content.ReadFromJsonAsync<SwapLessons.Response>(TestJson.Options))!;

        await using var db = await _factory.CreateDbContextAsync();
        var oldSeries = await db.LessonSeries.Where(s => s.Id == setup.ThursdaySeriesId || s.Id == setup.FridaySeriesId).ToListAsync();
        Assert.All(oldSeries, s => Assert.Equal(LessonSeriesStatus.Ended, s.Status));

        var feritSeries = await db.LessonSeries.SingleAsync(s => s.Id == swapped.FirstId);
        var emirSeries = await db.LessonSeries.SingleAsync(s => s.Id == swapped.SecondId);
        Assert.Equal(DayOfWeek.Friday, feritSeries.DayOfWeek);
        Assert.Equal(DayOfWeek.Thursday, emirSeries.DayOfWeek);
        Assert.Equal(new TimeOnly(15, 0), feritSeries.StartTime);

        // Takas haftasından itibaren Ferit yalnızca Cuma, Emir Ali yalnızca Perşembe.
        var feritFuture = await db.Lessons
            .Where(l => l.StudentId == setup.ThursdayStudentId && l.Status == LessonStatus.Normal &&
                        l.StartAt >= setup.ThursdayLesson.StartAt)
            .Select(l => l.StartAt).ToListAsync();
        Assert.NotEmpty(feritFuture);
        Assert.All(feritFuture, startAt => Assert.Equal(DayOfWeek.Friday, startAt.ToOffset(SchoolOffset).DayOfWeek));
        Assert.Contains(setup.FridayLesson.StartAt, feritFuture);

        var emirFuture = await db.Lessons
            .Where(l => l.StudentId == setup.FridayStudentId && l.Status == LessonStatus.Normal &&
                        l.StartAt >= setup.ThursdayLesson.StartAt)
            .Select(l => l.StartAt).ToListAsync();
        Assert.All(emirFuture, startAt => Assert.Equal(DayOfWeek.Thursday, startAt.ToOffset(SchoolOffset).DayOfWeek));
        Assert.Contains(setup.ThursdayLesson.StartAt, emirFuture);

        Assert.True(await db.NotificationJobs.AnyAsync(j =>
            j.Type == NotificationJobType.LessonScheduleChanged && j.ReferenceId == feritSeries.Id));
        Assert.Equal(2, await db.AuditLogs.CountAsync(log =>
            log.Action == "lesson_series.swapped" && (log.EntityId == feritSeries.Id || log.EntityId == emirSeries.Id)));
    }

    // Invariant: takasın bir tarafı çakışıyorsa hiçbir şey yazılmaz - yarım takas yok.
    [Fact]
    public async Task Swap_is_rejected_as_a_whole_when_one_side_would_collide()
    {
        // Ferit'in dersi 60 dk: Cuma 15:00'e geçince 16:00'ya kadar sürer.
        var setup = await ArrangeAsync(thursdayDuration: 60, fridayDuration: 45);

        // Aynı öğretmenin Cuma 15:45'te başka bir öğrencisi var.
        var instruments = await setup.Admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>("/api/instruments", TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");
        var blocker = (await (await setup.Admin.PostAsJsonAsync("/api/students",
                new Students.CreateRequest("Engel", "Ogrenci", new DateOnly(2015, 3, 3))))
            .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
        var blockerEnrollment = (await (await setup.Admin.PostAsJsonAsync($"/api/students/{blocker.Id}/enrollments",
                new Enrollments.CreateRequest(setup.TeacherId, piano.Id, new DateOnly(2026, 8, 1))))
            .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
        Assert.Equal(HttpStatusCode.Created, (await setup.Admin.PostAsJsonAsync("/api/lesson-series",
            new LessonSeriesFeatures.CreateRequest(blockerEnrollment.Id, DayOfWeek.Friday, new TimeOnly(15, 45), 45,
                new DateOnly(2026, 8, 3), null))).StatusCode);

        foreach (var scope in new[] { SwapLessons.SwapScope.Once, SwapLessons.SwapScope.Recurring })
        {
            var response = await setup.Admin.PostAsJsonAsync($"/api/lessons/{setup.ThursdayLesson.Id}/swap",
                new SwapLessons.Request(setup.FridayLesson.Id, scope));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        await using var db = await _factory.CreateDbContextAsync();
        var originals = await db.Lessons.Where(l => l.Id == setup.ThursdayLesson.Id || l.Id == setup.FridayLesson.Id).ToListAsync();
        Assert.All(originals, l => Assert.Equal(LessonStatus.Normal, l.Status));
        var series = await db.LessonSeries.Where(s => s.Id == setup.ThursdaySeriesId || s.Id == setup.FridaySeriesId).ToListAsync();
        Assert.All(series, s => Assert.Equal(LessonSeriesStatus.Active, s.Status));
    }

    [Fact]
    public async Task Teacher_cannot_swap_with_another_teachers_lesson()
    {
        var email = $"takas-{Guid.NewGuid():N}@test.local";
        var mine = await ArrangeAsync(teacherEmail: email);
        var theirs = await ArrangeAsync();

        var teacher = _factory.CreateClient();
        (await teacher.PostAsJsonAsync("/api/auth/login", new Login.Request(email, mine.TeacherPassword!))).EnsureSuccessStatusCode();

        var foreign = await teacher.PostAsJsonAsync($"/api/lessons/{mine.ThursdayLesson.Id}/swap",
            new SwapLessons.Request(theirs.FridayLesson.Id, SwapLessons.SwapScope.Once));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        // Kendi iki dersini takas edebilir.
        var own = await teacher.PostAsJsonAsync($"/api/lessons/{mine.ThursdayLesson.Id}/swap",
            new SwapLessons.Request(mine.FridayLesson.Id, SwapLessons.SwapScope.Once));
        Assert.True(own.StatusCode == HttpStatusCode.OK, await own.Content.ReadAsStringAsync());
    }
}
