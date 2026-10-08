using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Features;

namespace Abdera.Tests.Integration;

// Müsaitlik ekranı (GET /api/scheduling/open-slots). Sorgu gerçek HTTP üzerinden çağrılır:
// birden çok tabloyu birleştiren bir handler'ın SQL'e çevrilebildiği ancak böyle görülür
// (CLAUDE.md - çok tablolu sorgularda OrderBy sırası).
public class OpenSlotsFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public OpenSlotsFlowTests(AbderaWebApplicationFactory factory)
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

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Beklenmeyen durum: {(int)response.StatusCode} {response.StatusCode}, gövde: {body}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(body, TestJson.Options)!;
    }

    private static async Task<Teachers.CreateResponse> CreateTeacherAsync(HttpClient admin, string firstName, Guid instrumentId, string? email = null) =>
        await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync("/api/teachers",
            new Teachers.CreateRequest(firstName, "Musait", [instrumentId], email)));

    [Fact]
    public async Task Open_slots_are_the_availability_window_minus_active_series_and_skip_teachers_without_a_window()
    {
        var admin = await LoginAsync("admin@test.local", "Test1234!");
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var piano = instruments.Single(i => i.Code == "PIANO");
        var guitar = instruments.Single(i => i.Code == "GUITAR");

        var busyTeacher = (await CreateTeacherAsync(admin, "Dolu", piano.Id)).Teacher;
        var undefinedTeacher = (await CreateTeacherAsync(admin, "Tanimsiz", piano.Id)).Teacher;
        var guitarTeacher = (await CreateTeacherAsync(admin, "Gitarci", guitar.Id)).Teacher;

        foreach (var teacherId in new[] { busyTeacher.Id, guitarTeacher.Id })
        {
            var window = await admin.PostAsJsonAsync($"/api/teachers/{teacherId}/availability",
                new TeacherAvailabilities.CreateRequest(DayOfWeek.Tuesday, new TimeOnly(15, 0), new TimeOnly(18, 0)));
            Assert.Equal(HttpStatusCode.Created, window.StatusCode);
        }

        var student = await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync("/api/students",
            new Students.CreateRequest("Slot", "Ogrenci", new DateOnly(2015, 5, 5))));
        var enrollment = await ReadAsync<Enrollments.EnrollmentResponse>(await admin.PostAsJsonAsync(
            $"/api/students/{student.Id}/enrollments", new Enrollments.CreateRequest(busyTeacher.Id, piano.Id, new DateOnly(2026, 8, 1))));
        var series = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            enrollment.Id, DayOfWeek.Tuesday, new TimeOnly(16, 0), 45, new DateOnly(2026, 8, 18), null));
        Assert.Equal(HttpStatusCode.Created, series.StatusCode);

        var response = await ReadAsync<OpenSlots.Response>(await admin.GetAsync(
            $"/api/scheduling/open-slots?instrumentId={piano.Id}&durationMinutes=45&from=2026-10-06"));

        var busy = Assert.Single(response.Teachers, t => t.TeacherId == busyTeacher.Id);
        var starts = busy.Slots.Select(s => $"{s.DayOfWeek} {s.StartTime:HH\\:mm}").ToList();
        Assert.Equal(["Tuesday 15:00", "Tuesday 15:15", "Tuesday 16:45", "Tuesday 17:00", "Tuesday 17:15"], starts);
        Assert.Equal(1, busy.WeeklyLessonCount);
        Assert.Equal(180, busy.WeeklyAvailableMinutes);
        Assert.Equal(45, busy.WeeklyBookedMinutes);
        Assert.All(busy.Slots, s => Assert.Equal(new DateOnly(2026, 10, 6), s.FirstOpenDate));

        // Uygunluk tanımlamamış öğretmen sayılmaz, yalnızca adı döner.
        Assert.DoesNotContain(response.Teachers, t => t.TeacherId == undefinedTeacher.Id);
        Assert.Contains(response.TeachersWithoutAvailability, t => t.TeacherId == undefinedTeacher.Id);

        // Enstrüman filtresi: gitar öğretmeni piyano aramasında görünmez.
        Assert.DoesNotContain(response.Teachers, t => t.TeacherId == guitarTeacher.Id);
    }

    [Fact]
    public async Task Open_slots_reject_an_out_of_range_duration_and_are_closed_to_teachers()
    {
        var admin = await LoginAsync("admin@test.local", "Test1234!");
        var invalid = await admin.GetAsync("/api/scheduling/open-slots?durationMinutes=5");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var email = $"openslots-{Guid.NewGuid():N}@test.local";
        var created = await CreateTeacherAsync(admin, "Yetkisiz", instruments.Single(i => i.Code == "PIANO").Id, email);

        var teacher = await LoginAsync(email, created.TemporaryPassword!);
        var forbidden = await teacher.GetAsync("/api/scheduling/open-slots");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
