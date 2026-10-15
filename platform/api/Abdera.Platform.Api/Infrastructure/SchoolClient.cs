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
    Task<string?> SmokeTestAsync(School school, CancellationToken cancellationToken);
}

public class HttpSchoolClient(HttpClient http) : ISchoolClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SchoolTelemetry> GetTelemetryAsync(School school, string telemetryToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{school.PrimaryDomain}/api/instance/telemetry");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", telemetryToken);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SchoolTelemetry>(Json, cancellationToken))!;
    }

    public async Task<string?> SmokeTestAsync(School school, CancellationToken cancellationToken)
    {
        using var health = await http.GetAsync($"https://{school.PrimaryDomain}/health", cancellationToken);
        if (!health.IsSuccessStatusCode) return $"/health {(int)health.StatusCode}";
        using var login = await http.GetAsync($"https://{school.PrimaryDomain}/login", cancellationToken);
        if (!login.IsSuccessStatusCode) return $"/login {(int)login.StatusCode}";
        var config = await http.GetFromJsonAsync<JsonElement>($"https://{school.PrimaryDomain}/api/instance/config", Json, cancellationToken);
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
            12_000_000));

    public Task<string?> SmokeTestAsync(School school, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
