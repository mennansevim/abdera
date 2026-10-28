using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Instance.Features;
using Abdera.Api.Modules.People.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Abdera.Tests.Integration;

// Platformun yeni okulu kurduğu durum: veritabanı boş, Bootstrap yöneticisi yok, geliştirme
// verisi de yok (ortam Development değil). docs/multi-tenant/ARCHITECTURE.md §5.4.
public class EmptySchoolWebApplicationFactory : AbderaWebApplicationFactory
{
    public const string ProvisioningToken = "provisioning-token-0123456789abcdef0123456789";
    public const string TelemetryToken = "telemetry-token-0123456789abcdef0123456789abcd";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bootstrap:AdminEmail"] = "",
            ["Bootstrap:AdminPassword"] = "",
            ["Provisioning:Token"] = ProvisioningToken,
            ["Telemetry:Token"] = TelemetryToken,
            ["App:Version"] = "2026.10.15.9",
        }));
    }
}

public class InstanceProvisioningFlowTests : IClassFixture<EmptySchoolWebApplicationFactory>
{
    private readonly EmptySchoolWebApplicationFactory _factory;

    public InstanceProvisioningFlowTests(EmptySchoolWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static InstanceProvisioning.ProvisionRequest Request(string email = "mudur@deniz.test") => new(
        new InstanceProvisioning.AdminRequest(email, "Gecici-Parola-2026", "Deniz Müdür"),
        new InstanceProvisioning.ProfileRequest("Deniz Müzik Okulu", "Deniz", "#1d6fa5", null),
        new InstanceProvisioning.PolicyRequest(35m, 3, 12, 30, 14, new TimeOnly(10, 0), new TimeOnly(20, 0)),
        new InstanceProvisioning.BillingRequest(10m, 7.5m, 5),
        [
            new InstanceProvisioning.TariffRequest(CourseKind.Individual, 4, 3000m, "TRY", new DateOnly(2026, 9, 1)),
            new InstanceProvisioning.TariffRequest(CourseKind.Group, 4, 1500m, "TRY", new DateOnly(2026, 9, 1)),
        ]);

    private HttpClient Client(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // Tek test: kurulum bir kez yapılabildiği için sıra önemli (önce reddedilenler, sonra kurulum,
    // sonra tekrar ve farklı istek, en sonda telemetri).
    [Fact]
    public async Task Platform_provisions_an_empty_school_once_and_reads_only_counts()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(null).PostAsJsonAsync("/api/instance/provision", Request())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client("yanlis-jeton").PostAsJsonAsync("/api/instance/provision", Request())).StatusCode);
        // Telemetri jetonu kurulum ucunu açmaz.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Client(EmptySchoolWebApplicationFactory.TelemetryToken).PostAsJsonAsync("/api/instance/provision", Request())).StatusCode);

        var platform = Client(EmptySchoolWebApplicationFactory.ProvisioningToken);
        var invalid = await platform.PostAsJsonAsync("/api/instance/provision", Request() with
        {
            Admin = new InstanceProvisioning.AdminRequest("mudur@deniz.test", "kisa", null),
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var first = await platform.PostAsJsonAsync("/api/instance/provision", Request());
        Assert.True(first.StatusCode == HttpStatusCode.OK, await first.Content.ReadAsStringAsync());
        var firstBody = (await first.Content.ReadFromJsonAsync<InstanceProvisioning.ProvisionResponse>(TestJson.Options))!;
        Assert.False(firstBody.AlreadyProvisioned);

        // Platform cevabı kaybedip aynı isteği yeniden gönderirse hiçbir şey değişmez.
        var retry = await platform.PostAsJsonAsync("/api/instance/provision", Request());
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.True((await retry.Content.ReadFromJsonAsync<InstanceProvisioning.ProvisionResponse>(TestJson.Options))!.AlreadyProvisioned);

        // Kurulumdan sonra farklı bir istek kalıcı olarak reddedilir.
        Assert.Equal(HttpStatusCode.Conflict,
            (await platform.PostAsJsonAsync("/api/instance/provision", Request("baska@deniz.test"))).StatusCode);

        await using (var db = await _factory.CreateDbContextAsync())
        {
            var admin = await db.Users.SingleAsync();
            Assert.Equal("mudur@deniz.test", admin.Email);
            Assert.Equal(UserRole.Admin, admin.Role);
            Assert.True(admin.MustChangePassword);
            Assert.NotEqual(PasswordVerificationResult.Failed,
                new PasswordHasher<User>().VerifyHashedPassword(admin, admin.PasswordHash, "Gecici-Parola-2026"));

            var profile = await db.SchoolProfiles.SingleAsync();
            Assert.Equal("Deniz Müzik Okulu", profile.Name);
            var policy = await db.SchoolPolicies.SingleAsync();
            Assert.Equal(35m, policy.GroupLessonSharePercent);
            Assert.Equal(new TimeOnly(10, 0), policy.SchoolDayStart);
            var billing = await db.BillingSettings.SingleAsync();
            Assert.Equal(7.5m, billing.SiblingDiscountPercent);
            Assert.Equal(2, await db.TuitionRates.CountAsync(rate => rate.EffectiveUntil == null));
            // Tek kurulum kaydı; parola denetim kaydına yazılmaz.
            var record = await db.AuditLogs.SingleAsync(log => log.Action == InstanceProvisioning.ProvisionedAction);
            Assert.DoesNotContain("Gecici-Parola-2026", record.AfterJson);
        }

        // Telemetri yalnızca sayı döner; kurulum jetonu telemetriyi açmaz.
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Client(EmptySchoolWebApplicationFactory.ProvisioningToken).GetAsync("/api/instance/telemetry")).StatusCode);
        var telemetryResponse = await Client(EmptySchoolWebApplicationFactory.TelemetryToken).GetAsync("/api/instance/telemetry");
        Assert.Equal(HttpStatusCode.OK, telemetryResponse.StatusCode);
        var raw = await telemetryResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("mudur@deniz.test", raw);
        Assert.DoesNotContain("Deniz", raw);
        var telemetry = (await telemetryResponse.Content.ReadFromJsonAsync<InstanceTelemetry.TelemetryResponse>(TestJson.Options))!;
        Assert.Equal("2026.10.15.9", telemetry.Version);
        Assert.NotNull(telemetry.ProvisionedAt);
        Assert.Equal(0, telemetry.Counts.ActiveStudents);
        Assert.True(telemetry.DatabaseSizeBytes > 0);
        // Gözlem paneli: hata/uyarı yalnızca adet, veritabanı bağlantı ve migration bilgisi.
        Assert.NotNull(telemetry.Logs);
        Assert.True(telemetry.Logs.Errors24h >= 0);
        Assert.NotNull(telemetry.Database);
        Assert.True(telemetry.Database.Connections >= 1);
        Assert.False(string.IsNullOrEmpty(telemetry.Database.LastMigration));

        Assert.Equal(HttpStatusCode.Accepted,
            (await Client(EmptySchoolWebApplicationFactory.TelemetryToken).PostAsync("/api/instance/backup-now", null)).StatusCode);
    }
}

// Jeton tanımlı olmayan kurulumda (bugünkü canlı okul) platform uçları hiç yokmuş gibi davranır.
public class InstancePlatformEndpointsHiddenTests : IClassFixture<AbderaWebApplicationFactory>
{
    private readonly AbderaWebApplicationFactory _factory;

    public InstancePlatformEndpointsHiddenTests(AbderaWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Without_tokens_platform_endpoints_are_not_found()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "herhangi-bir-jeton-0123456789abcdef0123");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/instance/telemetry")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/instance/backup-now", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/instance/provision", new { })).StatusCode);
    }
}
