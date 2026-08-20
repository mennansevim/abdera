using System.Security.Cryptography;
using System.Text;

namespace Abdera.Api.Modules.Messaging.Domain;

// docs/00-master-prompt.md: "Validate the provider signature, such as X-Hub-Signature-256,
// before trusting the payload." Meta, header'da "sha256=<hex>" formatında, ham istek gövdesi
// (body) üzerinden App Secret ile hesaplanmış bir HMAC gönderir.
public static class WebhookSignatureVerifier
{
    public static bool IsValid(string rawBody, string? signatureHeader, string appSecret)
    {
        // appSecret boşsa (ör. WhatsApp__AppSecret ortam değişkeni tanımsız kalmışsa) HMAC'i
        // boş anahtarla hesaplamak deterministik/tahmin edilebilir bir sonuç üretir - bu da
        // imza doğrulamasını sessizce fail-open yapar. Bkz. Modules/Banking/Features/Webhooks.cs
        // VerifySharedSecret ile aynı desen.
        if (string.IsNullOrEmpty(appSecret))
            return false;

        if (string.IsNullOrWhiteSpace(signatureHeader) || !signatureHeader.StartsWith("sha256="))
            return false;

        var providedHex = signatureHeader["sha256=".Length..];
        var expectedHash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(rawBody));
        var expectedHex = Convert.ToHexStringLower(expectedHash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(providedHex.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(expectedHex));
    }
}
