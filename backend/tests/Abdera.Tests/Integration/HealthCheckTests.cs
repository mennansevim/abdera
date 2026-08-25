using System.Net;

namespace Abdera.Tests.Integration;

public class HealthCheckTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public HealthCheckTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_endpoint_returns_ok_when_database_is_reachable()
    {
        // Testin gerçekten şema oluşturduğundan emin ol - factory container'ı başlatır
        // ama migration'ları yalnızca CreateDbContextAsync çağrıldığında uygular.
        await using var db = await _factory.CreateDbContextAsync();

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Development_openapi_document_is_available_at_the_documented_path()
    {
        await using var db = await _factory.CreateDbContextAsync();

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"openapi\"", await response.Content.ReadAsStringAsync());
    }
}
