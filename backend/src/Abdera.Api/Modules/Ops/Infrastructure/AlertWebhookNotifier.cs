using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Abdera.Api.Modules.Ops.Domain;

namespace Abdera.Api.Modules.Ops.Infrastructure;

// Okulun sistem alarmlarını platforma iletir (docs/multi-tenant/ARCHITECTURE.md §5.5, A7):
// Alerts__WebhookUrl tanımlıysa sağlık bozulması, düzelmesi ve yedek hatası JSON olarak POST
// edilir; platform tek yerden haber verir. Tanımlı değilse (bugünkü tek okul) hiçbir şey yapmaz.
// E-posta alarmı ayrıca sürer. Gövde Alerts__WebhookSecret ile HMAC-SHA256 imzalanır.
// Alarm iletimi asla işi bozmaz: hata yalnızca loglanır.
public class AlertWebhookNotifier(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<AlertWebhookNotifier> logger)
{
    public const string HttpClientName = "alerts";
    public const string SignatureHeader = "X-Abdera-Signature";

    public async Task NotifyAsync(string kind, SystemHealthLevel level, string detail, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var url = config["Alerts:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            // Okul adı ya da kişi verisi gönderilmez; platform okulu slug'ından tanır.
            var body = JsonSerializer.Serialize(new
            {
                instance = config["App:InstanceSlug"],
                version = config["App:Version"],
                kind,
                level = level.ToString(),
                detail,
                at,
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            var secret = config["Alerts:WebhookSecret"];
            if (!string.IsNullOrEmpty(secret))
            {
                var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
                request.Headers.Add(SignatureHeader, $"sha256={Convert.ToHexString(signature).ToLowerInvariant()}");
            }

            using var client = httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(10);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Platform alarm ucu {Status} döndü ({Kind}).", (int)response.StatusCode, kind);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Platform alarm ucuna ulaşılamadı ({Kind}).", kind);
        }
    }
}
