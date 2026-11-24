using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Modules.Scheduling.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Tests.Integration;

// docs/10-decisions.md AM: 30 Eylül'de girilen program 8 hafta sonra takvimden boşaldı. Uzatma
// eksik günleri okul yılı sonuna kadar açar, var olan derse (taşınmış dahil) dokunmaz.
public class SeriesExtensionFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public SeriesExtensionFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void School_year_ends_in_june_and_rolls_over_in_july()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.Equal(new DateOnly(2027, 6, 30), SeriesExtension.SchoolYearEnd(new DateOnly(2026, 11, 24), config));
        Assert.Equal(new DateOnly(2027, 6, 30), SeriesExtension.SchoolYearEnd(new DateOnly(2027, 6, 30), config));
        Assert.Equal(new DateOnly(2028, 6, 30), SeriesExtension.SchoolYearEnd(new DateOnly(2027, 7, 1), config));
    }

    [Fact]
    public async Task Extension_fills_until_school_year_end_and_keeps_existing_and_moved_lessons()
    {
        var admin = _factory.CreateClient();
        (await admin.PostAsJsonAsync("/api/auth/login", new Login.Request("admin@test.local", "Test1234!"))).EnsureSuccessStatusCode();
        var instruments = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>("/api/instruments", TestJson.Options);
        var piano = instruments!.Single(i => i.Code == "PIANO");
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var teacher = (await (await admin.PostAsJsonAsync("/api/teachers",
                new Teachers.CreateRequest("Uzatma", $"Ogretmen{suffix}", [piano.Id], null)))
            .Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!.Teacher;

        async Task<(Guid StudentId, Guid SeriesId)> SeriesAsync(string name, DayOfWeek day)
        {
            var student = (await (await admin.PostAsJsonAsync("/api/students",
                    new Students.CreateRequest(name, suffix, new DateOnly(2015, 3, 3))))
                .Content.ReadFromJsonAsync<Students.StudentResponse>(TestJson.Options))!;
            var enrollment = (await (await admin.PostAsJsonAsync($"/api/students/{student.Id}/enrollments",
                    new Enrollments.CreateRequest(teacher.Id, piano.Id, new DateOnly(2026, 8, 1))))
                .Content.ReadFromJsonAsync<Enrollments.EnrollmentResponse>(TestJson.Options))!;
            var response = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
                enrollment.Id, day, new TimeOnly(15, 0), 45, new DateOnly(2026, 8, 3), null));
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            var series = (await response.Content.ReadFromJsonAsync<LessonSeriesFeatures.CreateResponse>(TestJson.Options))!;
            return (student.Id, series.Series.Id);
        }

        var kept = await SeriesAsync("Uzayan", DayOfWeek.Saturday);
        var ended = await SeriesAsync("Biten", DayOfWeek.Thursday);

        // Uzayan serinin 3. gelecek dersi aynı öğrenciyle ertesi güne taşınır: eski satırın seri bağı kopar.
        await using var db = await _factory.CreateDbContextAsync();
        var moved = (await db.Lessons.Where(l => l.LessonSeriesId == kept.SeriesId && l.StartAt > DateTimeOffset.UtcNow)
            .OrderBy(l => l.StartAt).ToListAsync()).Skip(2).First();
        var moveResponse = await admin.PatchAsJsonAsync($"/api/lessons/{moved.Id}",
            new UpdateLesson.Request(kept.StudentId, teacher.Id, moved.StartAt.AddDays(1), 45, LessonStatus.Normal));
        Assert.True(moveResponse.IsSuccessStatusCode, await moveResponse.Content.ReadAsStringAsync());

        // Biten seri: bugünden sonra bitirilmiş - uzatılmamalı.
        var endUntil = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14));
        (await admin.PatchAsJsonAsync($"/api/lesson-series/{ended.SeriesId}",
            new LessonSeriesFeatures.EndRequest(endUntil))).EnsureSuccessStatusCode();

        db.ChangeTracker.Clear();
        var beforeIds = await db.Lessons.Where(l => l.TeacherId == teacher.Id).Select(l => l.Id).ToListAsync();

        SeriesExtension.Result first;
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            first = await SeriesExtension.ExtendAllAsync(sp.GetRequiredService<AbderaDbContext>(),
                sp.GetRequiredService<IClock>(), sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<INotificationScheduler>());
        }
        Assert.True(first.LessonsCreated > 0);

        db.ChangeTracker.Clear();
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        DateOnly Local(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, tz).Date);

        // Var olan hiçbir ders silinmedi.
        var afterIds = await db.Lessons.Where(l => l.TeacherId == teacher.Id).Select(l => l.Id).ToListAsync();
        Assert.Empty(beforeIds.Except(afterIds));

        var keptLessons = await db.Lessons.Where(l => l.LessonSeriesId == kept.SeriesId).ToListAsync();
        var keptDates = keptLessons.Select(l => Local(l.StartAt)).ToList();
        Assert.Equal(keptDates.Count, keptDates.Distinct().Count());
        Assert.True(keptDates.Max() > first.Until.AddDays(-7) && keptDates.Max() <= first.Until);
        // Taşınan dersin eski günü (Cumartesi) yeniden açılmadı.
        Assert.DoesNotContain(Local(moved.StartAt), keptDates);

        var endedDates = await db.Lessons.Where(l => l.LessonSeriesId == ended.SeriesId)
            .Select(l => l.StartAt).ToListAsync();
        Assert.All(endedDates, at => Assert.True(Local(at) <= endUntil));

        // İdempotent: ikinci tur hiçbir şey açmaz.
        using (var scope = _factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var second = await SeriesExtension.ExtendAllAsync(sp.GetRequiredService<AbderaDbContext>(),
                sp.GetRequiredService<IClock>(), sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<INotificationScheduler>());
            Assert.Equal(0, second.LessonsCreated);
        }
    }
}
