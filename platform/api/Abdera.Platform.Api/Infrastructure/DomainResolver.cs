using System.Net;

namespace Abdera.Platform.Api.Infrastructure;

// Okulun kendi alan adının DNS'i sunucuya çözülüyor mu (CNAME de sonunda sunucunun IP'sine çözülür).
public interface IDomainResolver
{
    Task<IReadOnlyList<string>> ResolveAsync(string domain, CancellationToken cancellationToken);
}

public class DnsDomainResolver : IDomainResolver
{
    public async Task<IReadOnlyList<string>> ResolveAsync(string domain, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(domain, cancellationToken);
            return addresses.Select(a => a.IsIPv4MappedToIPv6 ? a.MapToIPv4().ToString() : a.ToString()).Distinct().ToList();
        }
        catch (System.Net.Sockets.SocketException)
        {
            return [];
        }
    }
}

// Geliştirme/test: alan adı başına ayarlanabilir sonuç; ayarlanmamışsa çözülmez (DNS kaydı yok).
public class FakeDomainResolver : IDomainResolver
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> _records = new(StringComparer.OrdinalIgnoreCase);
    public void Set(string domain, params string[] addresses) => _records[domain] = addresses;

    public Task<IReadOnlyList<string>> ResolveAsync(string domain, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(_records.TryGetValue(domain, out var addresses) ? addresses : []);
}
