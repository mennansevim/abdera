using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Provisioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Abdera.Platform.Tests;

// LicenseWatcher + operatör bildirimi: lisansı bitmek üzere ya da dolmuş okul alarm düşürür, aynı sorun
// çoğalmaz, kritik alarm panelin dışına (webhook) da gider. Kendi veritabanı.
public class PlatformLicenseAlertTests(PlatformLicenseAlertTests.Fixture factory) : IClassFixture<PlatformLicenseAlertTests.Fixture>
{
    public class Fixture : PlatformFactory
    {
        public RecordingNotifier Notifier { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddSingleton<IOperatorNotifier>(Notifier));
        }
    }

    public class RecordingNotifier : IOperatorNotifier
    {
        public List<OperatorNotice> Sent { get; } = [];
        public Task NotifyAsync(IReadOnlyCollection<OperatorNotice> notices, CancellationToken cancellationToken)
        {
            lock (Sent) Sent.AddRange(notices);
            return Task.CompletedTask;
        }
    }

    private async Task CreateAsync(HttpClient client, string slug, DateOnly licenseStart)
    {
        var body = JsonSerializer.SerializeToNode(PlatformFactory.SchoolRequest(slug, "2026.10.15.1"), PlatformFactory.Json)!.AsObject();
        body["package"]!["licenseStartsOn"] = licenseStart.ToString("yyyy-MM-dd");
        var created = await client.PostAsync("/api/schools", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        created.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Expiring_and_expired_licenses_raise_one_alert_each_and_reach_the_operator()
    {
        var client = await factory.OperatorAsync();
        await factory.RegisterReleaseAsync(client, "2026.10.15.1", 'a');
        await factory.AddHostAsync(client, "lisans-1");
        var today = DateOnly.FromDateTime(factory.Time.GetUtcNow().UtcDateTime);
        await CreateAsync(client, "bitiyor", today.AddDays(-340));
        await CreateAsync(client, "doldu", today.AddDays(-400));
        await CreateAsync(client, "taze", today);
        await factory.RunAllAsync();

        var watcher = factory.Services.GetRequiredService<LicenseWatcher>();
        await watcher.CheckAllAsync(CancellationToken.None);
        await watcher.CheckAllAsync(CancellationToken.None);

        var alerts = (await client.GetFromJsonAsync<List<JsonElement>>("/api/alerts", PlatformFactory.Json))!;
        Assert.Equal(2, alerts.Count);
        Assert.Contains(alerts, a => a.GetProperty("slug").GetString() == "bitiyor" && a.GetProperty("kind").GetString() == LicenseWatcher.ExpiringKind
            && a.GetProperty("level").GetString() == "warning");
        Assert.Contains(alerts, a => a.GetProperty("slug").GetString() == "doldu" && a.GetProperty("kind").GetString() == LicenseWatcher.ExpiredKind
            && a.GetProperty("level").GetString() == "critical");
        Assert.DoesNotContain(alerts, a => a.GetProperty("slug").GetString() == "taze");

        // İki tur, ama her sorun operatöre yalnızca bir kez bildirilir.
        Assert.Equal(2, factory.Notifier.Sent.Count);
        Assert.Contains(factory.Notifier.Sent, n => n.SchoolSlug == "doldu" && n.Level == "critical");
    }
}

public class OperatorNotifierTests
{
    private class Handler(List<string> bodies) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }

    private class Clients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static WebhookOperatorNotifier Create(List<string> bodies, Dictionary<string, string?> settings) =>
        new(new Clients(new Handler(bodies)), new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WebhookOperatorNotifier>.Instance);

    [Fact]
    public async Task Only_notices_at_or_above_the_minimum_level_are_sent_and_no_url_means_silence()
    {
        var bodies = new List<string>();
        var notices = new[]
        {
            new OperatorNotice("warning", "a", "k", "uyari"),
            new OperatorNotice("critical", "b", "k", "kritik"),
        };

        await Create(bodies, new()).NotifyAsync(notices, CancellationToken.None);
        Assert.Empty(bodies);

        var critical = Create(bodies, new() { ["Notifications:WebhookUrl"] = "https://hooks.example.test/x" });
        await critical.NotifyAsync(notices, CancellationToken.None);
        var sent = Assert.Single(bodies);
        Assert.Contains("kritik", sent);
        Assert.DoesNotContain("uyari", sent);

        bodies.Clear();
        await Create(bodies, new() { ["Notifications:WebhookUrl"] = "https://hooks.example.test/x", ["Notifications:MinLevel"] = "warning" })
            .NotifyAsync(notices, CancellationToken.None);
        Assert.Contains("uyari", Assert.Single(bodies));
    }
}
