using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;

namespace Abdera.Platform.Api.Infrastructure;

// Okul alt alan adı kaydı (§8). Wildcard yok: okul başına tek A kaydı.
public interface IDnsProvider
{
    Task UpsertARecordAsync(string fqdn, string ip, CancellationToken cancellationToken);
    Task DeleteRecordAsync(string fqdn, CancellationToken cancellationToken);
}

// Cloudflare API: Dns:Cloudflare:ApiToken (yalnızca ilgili bölgede DNS düzenleme yetkisi) ve ZoneId.
public class CloudflareDnsProvider(HttpClient http, IConfiguration config) : IDnsProvider
{
    private string Zone => config["Dns:Cloudflare:ZoneId"] ?? throw new InvalidOperationException("Dns:Cloudflare:ZoneId tanımlı değil.");

    private HttpRequestMessage Request(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, $"https://api.cloudflare.com/client/v4/zones/{Zone}/dns_records{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config["Dns:Cloudflare:ApiToken"]);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<string?> FindAsync(string fqdn, CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(Request(HttpMethod.Get, $"?type=A&name={Uri.EscapeDataString(fqdn)}"), cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var result = json.GetProperty("result");
        return result.GetArrayLength() > 0 ? result[0].GetProperty("id").GetString() : null;
    }

    public async Task UpsertARecordAsync(string fqdn, string ip, CancellationToken cancellationToken)
    {
        // İdempotent: kayıt varsa güncellenir, yoksa açılır. Proxy kapalı: Caddy sertifikayı HTTP-01 ile alır.
        var body = new { type = "A", name = fqdn, content = ip, ttl = 300, proxied = false };
        var id = await FindAsync(fqdn, cancellationToken);
        using var response = await http.SendAsync(
            id is null ? Request(HttpMethod.Post, "", body) : Request(HttpMethod.Put, $"/{id}", body), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteRecordAsync(string fqdn, CancellationToken cancellationToken)
    {
        var id = await FindAsync(fqdn, cancellationToken);
        if (id is null) return;
        using var response = await http.SendAsync(Request(HttpMethod.Delete, $"/{id}"), cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public class FakeDnsProvider : IDnsProvider
{
    public ConcurrentDictionary<string, string> Records { get; } = new();

    public Task UpsertARecordAsync(string fqdn, string ip, CancellationToken cancellationToken)
    {
        Records[fqdn] = ip;
        return Task.CompletedTask;
    }

    public Task DeleteRecordAsync(string fqdn, CancellationToken cancellationToken)
    {
        Records.TryRemove(fqdn, out _);
        return Task.CompletedTask;
    }
}
