using System.Net.Http.Json;
using System.Text.Json;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Provisioning;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Platform.Tests;

// CertificateWatcher (§3.3): yenilemesi duran sertifika alarm düşürür, aynı sorun alarmı çoğaltmaz.
// Kendi veritabanı (alarm sayıları başka testlerden etkilenmesin).
public class PlatformCertificateTests(PlatformCertificateTests.Fixture factory) : IClassFixture<PlatformCertificateTests.Fixture>
{
    public class Fixture : PlatformFactory;

    [Fact]
    public async Task Expiring_or_invalid_certificate_raises_one_alert_until_acknowledged()
    {
        var client = await factory.OperatorAsync();
        await factory.RegisterReleaseAsync(client, "2026.10.15.1", 'a');
        await factory.AddHostAsync(client, "tls-1");
        await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("biten", "2026.10.15.1"));
        await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("saglam", "2026.10.15.1"));
        await factory.CreateSchoolAsync(client, PlatformFactory.SchoolRequest("kapali", "2026.10.15.1"));
        await factory.RunAllAsync();

        var probe = (FakeCertificateProbe)factory.Services.GetRequiredService<ICertificateProbe>();
        var watcher = factory.Services.GetRequiredService<CertificateWatcher>();
        var now = factory.Time.GetUtcNow();
        probe.Set("biten.abdera.test", new CertificateInfo(now.AddDays(5), true));
        probe.Set("saglam.abdera.test", new CertificateInfo(now.AddDays(80), true));
        probe.Unreachable("kapali.abdera.test");

        await watcher.CheckAllAsync(CancellationToken.None);
        await watcher.CheckAllAsync(CancellationToken.None);

        var alerts = await AlertsAsync(client);
        var expiring = Assert.Single(alerts);
        Assert.Equal("biten", expiring.GetProperty("slug").GetString());
        Assert.Equal(CertificateWatcher.ExpiringKind, expiring.GetProperty("kind").GetString());
        Assert.Equal("warning", expiring.GetProperty("level").GetString());

        // Zinciri bozulan okul ayrı bir kritik alarm alır.
        probe.Set("saglam.abdera.test", new CertificateInfo(now.AddDays(80), false));
        await watcher.CheckAllAsync(CancellationToken.None);
        alerts = await AlertsAsync(client);
        Assert.Equal(2, alerts.Count);
        Assert.Contains(alerts, a => a.GetProperty("slug").GetString() == "saglam" && a.GetProperty("kind").GetString() == CertificateWatcher.InvalidKind
            && a.GetProperty("level").GetString() == "critical");

        // Operatör onaylar ama sorun sürer: bir sonraki turda yeniden düşer.
        (await client.PostAsync($"/api/alerts/{expiring.GetProperty("id").GetGuid()}/ack", null)).EnsureSuccessStatusCode();
        await watcher.CheckAllAsync(CancellationToken.None);
        alerts = await AlertsAsync(client);
        Assert.Equal(2, alerts.Count(a => a.GetProperty("slug").GetString() == "biten"));
        Assert.DoesNotContain(alerts, a => a.GetProperty("slug").GetString() == "kapali");
    }

    private static async Task<List<JsonElement>> AlertsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<JsonElement>>("/api/alerts", PlatformFactory.Json))!;
}
