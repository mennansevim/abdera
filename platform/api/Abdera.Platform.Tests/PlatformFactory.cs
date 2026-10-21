using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Persistence;
using Abdera.Platform.Api.Provisioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Abdera.Platform.Tests;

// Platform testleri: gerçek Postgres (SKIP LOCKED, kısmi unique index, xmin), sahte ajan/DNS/okul.
// İşçiler arka planda koşmaz; testler adımları RunAllAsync ile belirlenimli yürütür.
public class PlatformFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string OperatorEmail = "ops@abdera.test";
    public const string OperatorPassword = "Platform-Parola-2026";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("abdera_platform_test").WithUsername("platform").WithPassword("platform").Build();

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Time));
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Platform"] = _postgres.GetConnectionString(),
            ["Platform:BootstrapEmail"] = OperatorEmail,
            ["Platform:BootstrapPassword"] = OperatorPassword,
            ["Platform:RunWorkers"] = "false",
            ["Platform:PublicUrl"] = "https://platform.abdera.test",
            ["Dns:BaseDomain"] = "abdera.test",
            ["Provisioning:SmokeAttempts"] = "1",
            ["Rollouts:CanarySoakMinutes"] = "0",
            ["RateLimiting:LoginPermitLimit"] = "10000",
            ["Backup:R2:AccessKeyId"] = "r2-key",
            ["Backup:R2:SecretAccessKey"] = "r2-secret",
            ["Backup:R2:AccountId"] = "acc",
        }));
    }

    public FakeHostAgent Agent => (FakeHostAgent)Services.GetRequiredService<IHostAgent>();
    public FakeDnsProvider Dns => (FakeDnsProvider)Services.GetRequiredService<IDnsProvider>();

    public PlatformDbContext Db()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
    }

    // Bekleyen tüm işlemleri bitene ya da hata verene kadar yürütür.
    public async Task RunAllAsync()
    {
        var worker = Services.GetRequiredService<ProvisioningWorker>();
        for (var i = 0; i < 50 && await worker.RunNextAsync(CancellationToken.None); i++) { }
    }

    // TOTP'yi kurup doğrulanmış oturum açar. Aynı kod iki kez kabul edilmediği için her girişte
    // denetlenebilir saat bir TOTP adımı ileri alınır (gerçek 30 sn beklenmez).
    public async Task<HttpClient> OperatorAsync()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        Time.Advance(TimeSpan.FromSeconds(31));
        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = OperatorEmail,
            password = OperatorPassword,
            totpCode = Secret.Length == 0 ? null : Totp.CodeAt(Secret, Totp.StepAt(Time.GetUtcNow())),
        });
        login.EnsureSuccessStatusCode();
        if (Secret.Length == 0)
        {
            var setup = await (await client.PostAsync("/api/auth/totp/setup", null)).Content.ReadFromJsonAsync<JsonElement>();
            Secret = setup.GetProperty("secret").GetString()!;
            (await client.PostAsJsonAsync("/api/auth/totp/confirm", new { code = Totp.CodeAt(Secret, Totp.StepAt(Time.GetUtcNow())) })).EnsureSuccessStatusCode();
        }
        return client;
    }

    public string Secret { get; set; } = "";
    public ManualTime Time { get; } = new();

    public static string Image(string name, char digit) => $"ghcr.io/abdera/{name}@sha256:{new string(digit, 64)}";

    public static object[] Catalog =>
    [
        new { key = "core.people", name = "Öğrenci, veli, öğretmen", isCore = true, requires = Array.Empty<string>() },
        new { key = "core.billing", name = "Aidat", isCore = true, requires = new[] { "core.people" } },
        new { key = "core.scheduling", name = "Takvim", isCore = true, requires = new[] { "core.people" } },
        new { key = "progress", name = "Gelişim", isCore = false, requires = new[] { "core.scheduling" } },
        new { key = "groups", name = "Grup dersleri", isCore = false, requires = new[] { "core.scheduling", "core.billing" } },
        new { key = "finance", name = "Gelir-gider", isCore = false, requires = new[] { "core.billing" } },
        new { key = "profitability", name = "Kârlılık", isCore = false, requires = new[] { "finance" } },
        new { key = "whatsapp", name = "WhatsApp", isCore = false, requires = Array.Empty<string>() },
        new { key = "email", name = "E-posta", isCore = false, requires = Array.Empty<string>() },
    ];

    public static object[] Categories =>
    [
        new { key = "music", name = "Müzik", unitLabel = "Enstrüman", unitLabelPlural = "Enstrümanlar", suggestions = new[] { "Piyano", "Gitar" } },
        new { key = "dance", name = "Dans", unitLabel = "Dans türü", unitLabelPlural = "Dans türleri", suggestions = new[] { "Bale", "Hip hop" } },
        new { key = "other", name = "Diğer", unitLabel = "Ders", unitLabelPlural = "Dersler", suggestions = Array.Empty<string>() },
    ];

    public async Task RegisterReleaseAsync(HttpClient client, string version, char digit)
    {
        var response = await client.PostAsJsonAsync("/api/releases", new
        {
            version,
            apiImage = Image("abdera-api", digit),
            webImage = Image("abdera-web", digit),
            revision = "abc123",
            modules = Catalog,
            notes = (string?)null,
            categories = Categories,
        });
        if ((int)response.StatusCode >= 300) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
    }

    public static object SchoolRequest(string slug, string version, string[]? modules = null, Dictionary<string, string>? integrations = null, bool canary = false) => new
    {
        slug,
        displayName = $"{slug} Müzik Okulu",
        shortName = slug,
        primaryColor = "#1d6fa5",
        logoBase64 = (string?)null,
        legalName = (string?)null,
        contactEmail = (string?)null,
        customDomain = (string?)null,
        version,
        modules = modules ?? ["progress"],
        admin = new { email = $"mudur@{slug}.test", displayName = "Müdür" },
        policy = new
        {
            groupLessonSharePercent = 40m, maxWeeklyLessons = 4, makeupNoticeHours = 24, makeupCreditValidDays = 60,
            excusedMakeupWindowDays = 21, schoolDayStart = "09:00:00", schoolDayEnd = "21:00:00",
        },
        billing = new { multiCourseDiscountPercent = 5m, siblingDiscountPercent = 5m, dueDayOfMonth = 1 },
        tariffs = new[] { new { courseKind = "Individual", lessonsPerMonth = 4, monthlyAmount = 3000m, currency = "TRY", effectiveFrom = "2026-10-01" } },
        integrations = integrations ?? new Dictionary<string, string>(),
        isCanary = canary,
        // Kullanıcı paketi: 8 personel hesabı × 400 + seçilen her eklenti 100.
        package = new { staffSeats = 8, pricePerSeat = 400m, addonPrices = (modules ?? ["progress"]).Distinct().ToDictionary(m => m, _ => 100m) },
    };

    public async Task<Guid> AddHostAsync(HttpClient client, string name, int memoryMb = 65536)
    {
        var response = await client.PostAsJsonAsync("/api/hosts", new
        {
            name, address = $"{name}.hosts.test", sshPort = 22, sshUser = "root",
            hostKeyFingerprint = "SHA256:abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG", publicIp = "203.0.113.10", totalMemoryMb = memoryMb,
        });
        if ((int)response.StatusCode >= 300) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public async Task<Guid> CreateSchoolAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/schools", request);
        if ((int)response.StatusCode >= 300) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}

// Testlerin ileri alabildiği saat (TOTP adımları, kanarya bekleme süresi).
public class ManualTime : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
