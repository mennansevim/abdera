using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace Abdera.Platform.Api.Infrastructure;

// Okul alan adındaki TLS sertifikasının bitiş tarihi (§3.3 CertificateWatcher). Caddy sertifikayı kendisi
// yeniler; bu yalnızca yenilemenin sessizce durduğunu fark etmek için.
public record CertificateInfo(DateTimeOffset NotAfter, bool ChainValid);

public interface ICertificateProbe
{
    Task<CertificateInfo> GetAsync(string domain, CancellationToken cancellationToken);
}

public class TlsCertificateProbe : ICertificateProbe
{
    public async Task<CertificateInfo> GetAsync(string domain, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(domain, 443, timeout.Token);
        var chainValid = false;
        // Doğrulama hatası olsa da bağlantı kurulur: amaç sertifikayı okumak, geçersizliği ayrıca raporlamak.
        await using var ssl = new SslStream(tcp.GetStream(), false, (_, _, _, errors) =>
        {
            chainValid = errors == SslPolicyErrors.None;
            return true;
        });
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = domain }, timeout.Token);
        var certificate = ssl.RemoteCertificate as X509Certificate2 ?? new X509Certificate2(ssl.RemoteCertificate!);
        return new CertificateInfo(new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero), chainValid);
    }
}

// Geliştirme/test: alan adı başına ayarlanabilir sonuç; ayarlanmamışsa 60 gün geçerli.
public class FakeCertificateProbe(TimeProvider time) : ICertificateProbe
{
    private readonly ConcurrentDictionary<string, CertificateInfo> _results = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _unreachable = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string domain, CertificateInfo info) { _unreachable.TryRemove(domain, out _); _results[domain] = info; }
    public void Unreachable(string domain) => _unreachable[domain] = true;

    public Task<CertificateInfo> GetAsync(string domain, CancellationToken cancellationToken)
    {
        if (_unreachable.ContainsKey(domain)) throw new SocketException((int)SocketError.HostUnreachable);
        return Task.FromResult(_results.TryGetValue(domain, out var info) ? info : new CertificateInfo(time.GetUtcNow().AddDays(60), true));
    }
}
