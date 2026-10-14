using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Features;

namespace Abdera.Tests.Integration;

// Grup dersi (Resim): sınıf açılır, öğrenciler altına eklenir. Takvim ve çakışma kuralları
// gerçek HTTP üzerinden doğrulanır (çok tablolu sorgular - CLAUDE.md OrderBy notu).
public class LessonGroupFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public LessonGroupFlowTests(AbderaWebApplicationFactory factory)
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

    private static async Task<Students.StudentResponse> CreateStudentAsync(HttpClient admin, string firstName) =>
        await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync("/api/students",
            new Students.CreateRequest(firstName, "Resimci", new DateOnly(2015, 3, 3))));

    private static DateOnly NextSaturday()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        while (today.DayOfWeek != DayOfWeek.Saturday) today = today.AddDays(1);
        return today;
    }

    [Fact]
    public async Task Group_holds_up_to_capacity_shows_as_one_slot_on_the_calendar_and_blocks_other_courses_at_that_time()
    {
        var admin = await LoginAsync("admin@test.local", "Test1234!");
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var art = instruments.Single(i => i.Code == "ART");
        var piano = instruments.Single(i => i.Code == "PIANO");
        var teacher = (await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync("/api/teachers",
            new Teachers.CreateRequest("Ressam", $"Grup{Guid.NewGuid():N}"[..12], [art.Id, piano.Id], null)))).Teacher;

        var firstSaturday = NextSaturday();
        var group = await ReadAsync<LessonGroups.LessonGroupResponse>(await admin.PostAsJsonAsync("/api/lesson-groups",
            new LessonGroups.CreateRequest("Resim Cumartesi", teacher.Id, art.Id, DayOfWeek.Saturday, new TimeOnly(10, 0), firstSaturday)));
        Assert.Equal(90, group.DurationMinutes);
        Assert.Equal(6, group.Capacity);
        Assert.Empty(group.Members);

        // Boş grup bile saatini tutar: aynı öğretmene çakışan ikinci grup açılamaz.
        var overlappingGroup = await admin.PostAsJsonAsync("/api/lesson-groups",
            new LessonGroups.CreateRequest("Çakışan", teacher.Id, art.Id, DayOfWeek.Saturday, new TimeOnly(11, 0), firstSaturday));
        Assert.Equal(HttpStatusCode.Conflict, overlappingGroup.StatusCode);

        var students = new List<Students.StudentResponse>();
        for (var i = 0; i < 7; i++) students.Add(await CreateStudentAsync(admin, $"Uye{i}"));

        LessonGroups.AddMemberResponse? last = null;
        foreach (var student in students.Take(6))
        {
            last = await ReadAsync<LessonGroups.AddMemberResponse>(await admin.PostAsJsonAsync(
                $"/api/lesson-groups/{group.Id}/members", new LessonGroups.AddMemberRequest(student.Id)));
        }
        Assert.Equal(6, last!.Group.Members.Count);
        Assert.True(last.Generation.Created > 0);

        var full = await admin.PostAsJsonAsync($"/api/lesson-groups/{group.Id}/members", new LessonGroups.AddMemberRequest(students[6].Id));
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);
        var duplicate = await admin.PostAsJsonAsync($"/api/lesson-groups/{group.Id}/members", new LessonGroups.AddMemberRequest(students[0].Id));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Üyenin kurs kaydı Grup türündedir (aidat Grup tarifesinden).
        var enrollments = await ReadAsync<List<Enrollments.EnrollmentResponse>>(await admin.GetAsync($"/api/students/{students[0].Id}/enrollments"));
        Assert.Equal(CourseKind.Group, Assert.Single(enrollments).CourseKind);

        // Takvim: ilk cumartesi 10:00-11:30 arası, aynı grupta altı öğrenci.
        var from = firstSaturday.ToDateTime(TimeOnly.MinValue);
        var calendar = await ReadAsync<List<Calendar.LessonResponse>>(await admin.GetAsync(
            $"/api/calendar?from={from:yyyy-MM-dd}T00:00:00Z&to={from.AddDays(1):yyyy-MM-dd}T00:00:00Z&teacherId={teacher.Id}"));
        Assert.Equal(6, calendar.Count);
        Assert.All(calendar, lesson =>
        {
            Assert.Equal(group.Id, lesson.LessonGroupId);
            Assert.Equal("Resim Cumartesi", lesson.LessonGroupName);
            Assert.Equal(6, lesson.LessonGroupCapacity);
            Assert.Equal(TimeSpan.FromMinutes(90), lesson.EndAt - lesson.StartAt);
        });
        Assert.Single(calendar.Select(l => l.StartAt).Distinct());

        // Başka bir kurs (bireysel piyano) grubun saatine giremez.
        var outsider = students[6];
        var pianoEnrollment = await ReadAsync<Enrollments.EnrollmentResponse>(await admin.PostAsJsonAsync(
            $"/api/students/{outsider.Id}/enrollments", new Enrollments.CreateRequest(teacher.Id, piano.Id, firstSaturday)));
        var clash = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            pianoEnrollment.Id, DayOfWeek.Saturday, new TimeOnly(11, 0), 45, firstSaturday, null));
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        var afterGroup = await admin.PostAsJsonAsync("/api/lesson-series", new LessonSeriesFeatures.CreateRequest(
            pianoEnrollment.Id, DayOfWeek.Saturday, new TimeOnly(11, 30), 45, firstSaturday, null));
        Assert.Equal(HttpStatusCode.Created, afterGroup.StatusCode);

        // Üyenin serisi tek başına taşınamaz - saat grubundan gelir.
        var memberSeriesId = last.Group.Members[0].SeriesId;
        var move = await admin.PostAsJsonAsync($"/api/lesson-series/{memberSeriesId}/reschedule",
            new LessonSeriesFeatures.RescheduleRequest(DayOfWeek.Sunday, new TimeOnly(10, 0), 90, null));
        Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);

        // Kapasite üye sayısının altına inemez; üyeli grup kapatılamaz.
        var shrink = await admin.PatchAsJsonAsync($"/api/lesson-groups/{group.Id}", new LessonGroups.UpdateRequest(null, 4));
        Assert.Equal(HttpStatusCode.Conflict, shrink.StatusCode);
        var end = await admin.PostAsync($"/api/lesson-groups/{group.Id}/end", null);
        Assert.Equal(HttpStatusCode.Conflict, end.StatusCode);

        // Üyeyi çıkarmak kurs kaydını bitirmektir; boşalan yere yeni öğrenci girer.
        var member = last.Group.Members[0];
        var removed = await admin.PostAsJsonAsync($"/api/students/{member.StudentId}/enrollments/{member.EnrollmentId}/end",
            new Enrollments.EndRequest(EnrollmentEndReason.ScheduleConflict, null));
        Assert.True(removed.IsSuccessStatusCode);
        var groups = await ReadAsync<List<LessonGroups.LessonGroupResponse>>(await admin.GetAsync("/api/lesson-groups"));
        Assert.Equal(5, groups.Single(g => g.Id == group.Id).Members.Count);
    }

    [Fact]
    public async Task Teacher_sees_only_own_groups_and_cannot_create_one()
    {
        var admin = await LoginAsync("admin@test.local", "Test1234!");
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var art = instruments.Single(i => i.Code == "ART");
        var email = $"resim-{Guid.NewGuid():N}@test.local";
        var created = await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync("/api/teachers",
            new Teachers.CreateRequest("Kendi", "Grubu", [art.Id], email)));
        var other = (await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync("/api/teachers",
            new Teachers.CreateRequest("Baska", "Ressam", [art.Id], null)))).Teacher;

        var mine = await ReadAsync<LessonGroups.LessonGroupResponse>(await admin.PostAsJsonAsync("/api/lesson-groups",
            new LessonGroups.CreateRequest("Benim", created.Teacher.Id, art.Id, DayOfWeek.Friday, new TimeOnly(17, 0), NextSaturday())));
        await ReadAsync<LessonGroups.LessonGroupResponse>(await admin.PostAsJsonAsync("/api/lesson-groups",
            new LessonGroups.CreateRequest("Onun", other.Id, art.Id, DayOfWeek.Friday, new TimeOnly(17, 0), NextSaturday())));

        var teacher = await LoginAsync(email, created.TemporaryPassword!);
        var visible = await ReadAsync<List<LessonGroups.LessonGroupResponse>>(await teacher.GetAsync("/api/lesson-groups"));
        Assert.Equal(mine.Id, Assert.Single(visible).Id);

        var forbidden = await teacher.PostAsJsonAsync("/api/lesson-groups",
            new LessonGroups.CreateRequest("Yetkisiz", created.Teacher.Id, art.Id, DayOfWeek.Monday, new TimeOnly(17, 0), NextSaturday()));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
