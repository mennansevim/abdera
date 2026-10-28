using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Abdera.Platform.Api.Domain;

namespace Abdera.Platform.Api.Infrastructure;

// Okulun dışarıdan görünen uçları: duman testi (§6 adım 11) ve telemetri (§3.3).
public interface ISchoolClient
{
    Task<SchoolTelemetry> GetTelemetryAsync(School school, string telemetryToken, CancellationToken cancellationToken);
    // https://<alan>/health 200 ve /api/instance/config okulun adını taşıyor mu.
    // domain verilirse okulun o adresi denenir (bağlanan özel alan adı).
    Task<string?> SmokeTestAsync(School school, CancellationToken cancellationToken, string? domain = null);
}

public class HttpSchoolClient(HttpClient http, IConfiguration config) : ISchoolClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // SchoolClient:HttpsPort yalnızca yerel test ortamı (Caddy 8443'te); production'da reddedilir.
    private string Base(School school, string? domain = null) => config["SchoolClient:HttpsPort"] is { Length: > 0 } port
        ? $"https://{domain ?? school.PrimaryDomain}:{port}"
        : $"https://{domain ?? school.PrimaryDomain}";

    public async Task<SchoolTelemetry> GetTelemetryAsync(School school, string telemetryToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base(school)}/api/instance/telemetry");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", telemetryToken);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SchoolTelemetry>(Json, cancellationToken))!;
    }

    public async Task<string?> SmokeTestAsync(School school, CancellationToken cancellationToken, string? domain = null)
    {
        var baseUrl = Base(school, domain);
        using var health = await http.GetAsync($"{baseUrl}/health", cancellationToken);
        if (!health.IsSuccessStatusCode) return $"/health {(int)health.StatusCode}";
        using var login = await http.GetAsync($"{baseUrl}/login", cancellationToken);
        if (!login.IsSuccessStatusCode) return $"/login {(int)login.StatusCode}";
        var config = await http.GetFromJsonAsync<JsonElement>($"{baseUrl}/api/instance/config", Json, cancellationToken);
        var name = config.GetProperty("profile").GetProperty("name").GetString();
        return name == school.DisplayName ? null : $"okul adı '{name}' görünüyor, '{school.DisplayName}' bekleniyordu";
    }
}

public class FakeSchoolClient : ISchoolClient
{
    public Task<SchoolTelemetry> GetTelemetryAsync(School school, string telemetryToken, CancellationToken cancellationToken) =>
        Task.FromResult(new SchoolTelemetry(
            school.AppVersion,
            new SchoolTelemetry.HealthPart("Healthy", null),
            new SchoolTelemetry.CountsPart(0, 0, 0),
            new SchoolTelemetry.BackupPart(DateTimeOffset.UtcNow.AddHours(-3)),
            new SchoolTelemetry.NotificationsPart(0, 0),
            12_000_000,
            new SchoolTelemetry.LogsPart(0, 3, null),
            new SchoolTelemetry.DatabasePart(4, 0, "20261028000000_Fake")));

    public Task<string?> SmokeTestAsync(School school, CancellationToken cancellationToken, string? domain = null) => Task.FromResult<string?>(null);
}

// Yerel test ortamı: okullar Caddy'nin kendi (internal) sertifika otoritesiyle yayında. O kök sertifika
// verilirse yalnızca ona zincirlenen sertifikalar kabul edilir (doğrulama kapatılmaz). Production'da
// TrustedRootCaPath boş olmak zorunda (Program.cs), orada sistem güven deposu kullanılır.
public static class SchoolTls
{
    public static HttpMessageHandler Handler(IConfiguration config)
    {
        var handler = new SocketsHttpHandler();
        if (config["SchoolClient:TrustedRootCaPath"] is { Length: > 0 } path)
        {
            var root = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(path);
            handler.SslOptions.CertificateChainPolicy = new System.Security.Cryptography.X509Certificates.X509ChainPolicy
            {
                TrustMode = System.Security.Cryptography.X509Certificates.X509ChainTrustMode.CustomRootTrust,
                CustomTrustStore = { root },
                RevocationMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
            };
        }
        return handler;
    }
}
