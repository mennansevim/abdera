using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Abdera.Api.Modules.People.Features;

namespace Abdera.Tests.Integration;

public class TeacherPasswordResetFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public TeacherPasswordResetFlowTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new Login.Request("admin@test.local", "Test1234!"));
        response.EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Admin_resets_teacher_password_and_only_the_new_temporary_password_works()
    {
        using var admin = await CreateAdminClientAsync();
        var instruments = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>(
            "/api/instruments", TestJson.Options);
        var piano = instruments!.Single(item => item.Code == "PIANO");
        var email = $"reset.{Guid.NewGuid():N}@test.local";

        var createResponse = await admin.PostAsJsonAsync(
            "/api/teachers",
            new Teachers.CreateRequest("Şifre", "Sıfırlama", [piano.Id], email));
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;

        using var teacher = _factory.CreateClient();
        var initialLogin = await teacher.PostAsJsonAsync(
            "/api/auth/login", new Login.Request(email, created.TemporaryPassword!));
        Assert.Equal(HttpStatusCode.OK, initialLogin.StatusCode);

        var forbidden = await teacher.PostAsync(
            $"/api/teachers/{created.Teacher.Id}/reset-password", null);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var resetResponse = await admin.PostAsync(
            $"/api/teachers/{created.Teacher.Id}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        var reset = (await resetResponse.Content.ReadFromJsonAsync<ResetPassword.Response>(TestJson.Options))!;
        Assert.False(string.IsNullOrWhiteSpace(reset.TemporaryPassword));
        Assert.NotEqual(created.TemporaryPassword, reset.TemporaryPassword);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await teacher.GetAsync("/api/auth/me")).StatusCode);

        using var freshTeacher = _factory.CreateClient();
        var oldPasswordLogin = await freshTeacher.PostAsJsonAsync(
            "/api/auth/login", new Login.Request(email, created.TemporaryPassword!));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);

        var newPasswordLogin = await freshTeacher.PostAsJsonAsync(
            "/api/auth/login", new Login.Request(email, reset.TemporaryPassword));
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);
        var login = await newPasswordLogin.Content.ReadFromJsonAsync<Login.Response>(TestJson.Options);
        Assert.True(login!.MustChangePassword);
    }

    [Fact]
    public async Task Teacher_without_a_login_account_cannot_have_a_password_reset()
    {
        using var admin = await CreateAdminClientAsync();
        var instruments = await admin.GetFromJsonAsync<List<Instruments.InstrumentResponse>>(
            "/api/instruments", TestJson.Options);
        var piano = instruments!.Single(item => item.Code == "PIANO");

        var createResponse = await admin.PostAsJsonAsync(
            "/api/teachers",
            new Teachers.CreateRequest("Hesapsız", "Öğretmen", [piano.Id], null));
        var created = (await createResponse.Content.ReadFromJsonAsync<Teachers.CreateResponse>(TestJson.Options))!;

        var resetResponse = await admin.PostAsync(
            $"/api/teachers/{created.Teacher.Id}/reset-password", null);
        Assert.Equal(HttpStatusCode.Conflict, resetResponse.StatusCode);
        Assert.Contains("giriş hesabı yok", await resetResponse.Content.ReadAsStringAsync());
    }
}
