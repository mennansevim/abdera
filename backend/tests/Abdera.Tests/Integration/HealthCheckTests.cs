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
}
