using System.Security.Cryptography;
using System.Text;

namespace Abdera.Api.Modules.Instance.Features;

// Yalnızca Abdera Platform'un çağırdığı uçların erişim denetimi (docs/multi-tenant/ARCHITECTURE.md §5.4).
// Jeton okulun ortam değişkenindedir, platformda şifreli saklanır. Tanımlı değilse uç hiç yokmuş
// gibi 404 döner - bugünkü tek okullu kurulumda bu uçlar görünmez.
public static class PlatformAccess
{
    public const string ProvisioningTokenKey = "Provisioning:Token";
    public const string TelemetryTokenKey = "Telemetry:Token";

    // null: erişim var. Aksi halde döndürülecek sonuç (404 ya da 401).
    public static IResult? Check(HttpContext http, IConfiguration config, string tokenKey)
    {
        var expected = config[tokenKey];
        if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32) return Results.NotFound();

        var header = http.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal)) return Results.Unauthorized();

        // Sabit zamanlı karşılaştırma: uzunluk farkı da sızmasın diye özetler karşılaştırılır.
        var given = SHA256.HashData(Encoding.UTF8.GetBytes(header[prefix.Length..].Trim()));
        var wanted = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(given, wanted) ? null : Results.Unauthorized();
    }
}
