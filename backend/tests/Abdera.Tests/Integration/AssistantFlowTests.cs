using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Assistant.Domain;
using Abdera.Api.Modules.Assistant.Features;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.People.Features;

namespace Abdera.Tests.Integration;

// POST /api/assistant/resolve (docs/10-decisions.md AE). Ad eşleme veritabanındaki gerçek
// öğrenci listesiyle ve oturumun rol kapsamıyla yapılır; bu yüzden HTTP üzerinden sınanır.
public class AssistantFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public AssistantFlowTests(AbderaWebApplicationFactory factory)
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

    private static async Task<ResolveIntent.Response> ResolveAsync(HttpClient client, string text, Guid? studentId = null) =>
        await ReadAsync<ResolveIntent.Response>(await client.PostAsJsonAsync("/api/assistant/resolve", new ResolveIntent.Request(text, studentId)));

    private record School(HttpClient Admin, HttpClient PianoTeacher, Guid Sevim, Guid Kaya);

    // Aynı ilk adı taşıyan iki öğrenci: Sevim piyano öğretmeninin, Kaya gitar öğretmeninin.
    // Sınıftaki testler tek veritabanını paylaşır; her test kendi ilk adını verir ki başka
    // testin eklediği öğrenciler eşleşmeye karışmasın.
    private async Task<School> SeedAsync(string firstName)
    {
        var admin = await LoginAsync("admin@test.local", "Test1234!");
        var instruments = await ReadAsync<List<Instruments.InstrumentResponse>>(await admin.GetAsync("/api/instruments"));
        var piano = instruments.Single(i => i.Code == "PIANO");
        var guitar = instruments.Single(i => i.Code == "GUITAR");

        var pianoEmail = $"asistan-piyano-{Guid.NewGuid():N}@test.local";
        var pianoTeacher = await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync("/api/teachers",
            new Teachers.CreateRequest("Selin", "Arslan", [piano.Id], pianoEmail)));
        var guitarTeacher = await ReadAsync<Teachers.CreateResponse>(await admin.PostAsJsonAsync("/api/teachers",
            new Teachers.CreateRequest("Can", "Yildiz", [guitar.Id], null)));

        var sevim = await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync("/api/students",
            new Students.CreateRequest(firstName, "Sevim", new DateOnly(2014, 3, 3))));
        var kaya = await ReadAsync<Students.StudentResponse>(await admin.PostAsJsonAsync("/api/students",
            new Students.CreateRequest(firstName, "Kaya", new DateOnly(2013, 4, 4))));

        await ReadAsync<Enrollments.EnrollmentResponse>(await admin.PostAsJsonAsync($"/api/students/{sevim.Id}/enrollments",
            new Enrollments.CreateRequest(pianoTeacher.Teacher.Id, piano.Id, new DateOnly(2026, 9, 1))));
        await ReadAsync<Enrollments.EnrollmentResponse>(await admin.PostAsJsonAsync($"/api/students/{kaya.Id}/enrollments",
            new Enrollments.CreateRequest(guitarTeacher.Teacher.Id, guitar.Id, new DateOnly(2026, 9, 1))));

        var teacher = await LoginAsync(pianoEmail, pianoTeacher.TemporaryPassword!);
        return new School(admin, teacher, sevim.Id, kaya.Id);
    }

    [Fact]
    public async Task Admin_gets_the_intent_the_matching_students_and_their_courses()
    {
        var school = await SeedAsync("Mennan");

        var progress = await ResolveAsync(school.Admin, "Mennan'ın gelişimini getir");
        Assert.Equal(AssistantIntentKind.Progress, progress.Kind);
        Assert.True(progress.Allowed);
        Assert.Equal(
            new[] { school.Kaya, school.Sevim }.Order(),
            progress.Students.Select(s => s.StudentId).Order());

        var move = await ResolveAsync(school.Admin, "Mennan Sevim'in perşembe dersini 17:00'ye al");
        Assert.Equal(AssistantIntentKind.Reschedule, move.Kind);
        var student = Assert.Single(move.Students);
        Assert.Equal("Mennan Sevim", student.FullName);
        var course = Assert.Single(student.Courses);
        Assert.Equal("Selin Arslan", course.TeacherName);
        Assert.NotNull(move.Move);
        Assert.Equal(DayOfWeek.Thursday, move.Move.SourceDate?.DayOfWeek);
        Assert.Equal(new TimeOnly(17, 0), move.Move.TargetTime);

        var dues = await ResolveAsync(school.Admin, "Aidat günü gelenleri listele");
        Assert.Equal(AssistantIntentKind.Dues, dues.Kind);
        Assert.True(dues.Allowed);
        Assert.Empty(dues.Students);

        var navigate = await ResolveAsync(school.Admin, "Yoklamayı nereden alırım?");
        Assert.Equal(AssistantIntentKind.Navigate, navigate.Kind);
        Assert.Equal("/dashboard/attendance", navigate.Navigation?.Path);
    }

    [Fact]
    public async Task Teacher_matches_only_own_students_and_is_told_when_something_is_admin_only()
    {
        var school = await SeedAsync("Nehir");

        var progress = await ResolveAsync(school.PianoTeacher, "Nehir'in gelişimini getir");
        Assert.Equal([school.Sevim], progress.Students.Select(s => s.StudentId));

        var otherTeachersStudent = await ResolveAsync(school.PianoTeacher, "Nehir Kaya'nın dersleri");
        Assert.DoesNotContain(otherTeachersStudent.Students, s => s.StudentId == school.Kaya);

        var dues = await ResolveAsync(school.PianoTeacher, "Aidat günü gelenleri listele");
        Assert.Equal(AssistantIntentKind.Dues, dues.Kind);
        Assert.False(dues.Allowed);

        var adminScreen = await ResolveAsync(school.PianoTeacher, "Fiyat politikası nerede?");
        Assert.Equal(AssistantIntentKind.Navigate, adminScreen.Kind);
        Assert.False(adminScreen.Allowed);

        var ownScreen = await ResolveAsync(school.PianoTeacher, "Yoklamayı nereden alırım?");
        Assert.True(ownScreen.Allowed);
    }

    [Fact]
    public async Task A_teacher_named_with_full_name_is_not_matched_as_the_student_sharing_the_surname()
    {
        // Piyano öğretmeni "Selin Arslan" (SeedAsync); soyadı ortak bir öğrenci açılır.
        var school = await SeedAsync("Tuna");
        var arslan = await ReadAsync<Students.StudentResponse>(await school.Admin.PostAsJsonAsync("/api/students",
            new Students.CreateRequest("Kuzey", "Arslan", new DateOnly(2014, 1, 1))));

        var byTeacher = await ResolveAsync(school.Admin, "Selin Arslan'ın dersleri");
        Assert.DoesNotContain(byTeacher.Students, s => s.StudentId == arslan.Id);

        var byStudent = await ResolveAsync(school.Admin, "Arslan'ın dersleri");
        Assert.Contains(byStudent.Students, s => s.StudentId == arslan.Id);
    }

    [Fact]
    public async Task A_weekday_target_is_returned_with_its_weekday_and_a_shift_in_minutes()
    {
        var school = await SeedAsync("Umut");

        var weekday = await ResolveAsync(school.Admin, "Umut Sevim'in dersini cumaya taşı");
        Assert.Equal(DayOfWeek.Friday, weekday.Move?.TargetWeekday);
        Assert.False(weekday.Move?.TargetNextWeek);

        var date = await ResolveAsync(school.Admin, "Umut Sevim'in dersini 6 Kasım'a taşı");
        Assert.Null(date.Move?.TargetWeekday);
        Assert.Equal((11, 6), (date.Move?.TargetDate?.Month, date.Move?.TargetDate?.Day));

        var shift = await ResolveAsync(school.Admin, "Umut Sevim'in dersini yarım saat ileri al");
        Assert.Equal(30, shift.Move?.ShiftMinutes);
    }

    [Fact]
    public async Task Context_student_is_used_only_when_the_text_names_nobody_and_the_student_is_visible()
    {
        var school = await SeedAsync("Pelin");

        var own = await ResolveAsync(school.PianoTeacher, "gelişimini getir", school.Sevim);
        Assert.Equal([school.Sevim], own.Students.Select(s => s.StudentId));

        var hidden = await ResolveAsync(school.PianoTeacher, "gelişimini getir", school.Kaya);
        Assert.Empty(hidden.Students);

        var named = await ResolveAsync(school.Admin, "Pelin Kaya'nın gelişimi", school.Sevim);
        Assert.Equal([school.Kaya], named.Students.Select(s => s.StudentId));
    }

    [Fact]
    public async Task Empty_or_too_long_text_is_rejected_and_guests_are_unauthorized()
    {
        var admin = await LoginAsync("admin@test.local", "Test1234!");

        var empty = await admin.PostAsJsonAsync("/api/assistant/resolve", new ResolveIntent.Request("  ", null));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var tooLong = await admin.PostAsJsonAsync("/api/assistant/resolve", new ResolveIntent.Request(new string('a', ResolveIntent.MaxTextLength + 1), null));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var anonymous = await _factory.CreateClient().PostAsJsonAsync("/api/assistant/resolve", new ResolveIntent.Request("Merhaba", null));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }
}
