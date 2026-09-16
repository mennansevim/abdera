using System.Net;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Features;
using Microsoft.AspNetCore.Mvc;

namespace Abdera.Tests.Integration;

// Shared/GlobalExceptionHandler.cs - modüle özel olmayan, tüm domain doğrulama hatalarının
// ortak HTTP sözleşmesi. Buradaki tek gerçek bug: ArgumentException.Message'ın .NET tarafından
// otomatik eklenen " (Parameter 'x')" son ekini kullanıcıya sızdırması.
public class ErrorHandlingFlowTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public ErrorHandlingFlowTests(AbderaWebApplicationFactory factory)
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

    [Fact]
    public async Task ArgumentException_detail_does_not_leak_the_raw_parameter_name()
    {
        var admin = await CreateAdminClientAsync();

        // Student.ValidateBirthDate 120 yıldan eski bir tarihi ArgumentException(message, nameof(birthDate))
        // ile reddeder - Detail'de ".NET"in eklediği "(Parameter 'birthDate')" hiç görünmemeli.
        var response = await admin.PostAsJsonAsync("/api/students", new { firstName = "Eski", lastName = "Ogrenci", birthDate = new DateOnly(1500, 1, 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options);
        Assert.Equal("Doğum tarihi geçersiz görünüyor.", problem!.Detail);
        Assert.DoesNotContain("Parameter", problem.Detail);
    }
}
