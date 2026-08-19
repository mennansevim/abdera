using Abdera.Api.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace Abdera.Tests.Integration;

// docs/09-testing.md: Testcontainers yalnizca gercek Postgres davranisi gerektiren
// entegrasyon testlerinde kullanilir (migration, SKIP LOCKED, unique kisit, idempotency).
// Bu fabrika o testler icin gercek bir Postgres container'i ayaga kaldirir.
public class AbderaWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("abdera_test")
        .WithUsername("abdera_test")
        .WithPassword("abdera_test")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                ["WhatsApp:Provider"] = "Fake",
                ["Bootstrap:AdminEmail"] = "admin@test.local",
                ["Bootstrap:AdminPassword"] = "Test1234!",
            });
        });
    }

    public async Task<AbderaDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<AbderaDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        var context = new AbderaDbContext(options);
        await context.Database.MigrateAsync();
        return context;
    }
}
