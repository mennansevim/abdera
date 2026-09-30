using System.Net.Http.Json;
using Abdera.Api.Modules.Messaging.Domain;
using Microsoft.Extensions.Options;

namespace Abdera.Api.Modules.Messaging.Infrastructure;

public class WhatsAppOptions
{
    public string Provider { get; set; } = "Fake"; // Fake | Cloud
    public string PhoneNumberId { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string ApiVersion { get; set; } = "v21.0";
}

// WhatsApp__Provider=Cloud. Meta WhatsApp Business Cloud API - gerçek gönderim.
// docs/06-whatsapp.md - SendTemplateAsync onaylı şablon kullanır (business-initiated);
// SendFreeTextAsync yalnızca 24 saatlik pencere açıkken kullanılabilir - bu kontrol
// çağıran use-case'in sorumluluğu (docs/10-decisions.md A7), burada zorlanmaz.
public class CloudApiWhatsAppClient(HttpClient httpClient, IOptions<WhatsAppOptions> options, ILogger<CloudApiWhatsAppClient> logger)
    : IWhatsAppClient
{
    private readonly WhatsAppOptions _options = options.Value;

    public Task<WhatsAppSendResult> SendTemplateAsync(
        string toPhoneNumber,
        string templateName,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string>? buttonPayloads = null,
        CancellationToken cancellationToken = default)
    {
        var components = new List<object>
        {
            new
            {
                type = "body",
                parameters = parameters.Select(p => new { type = "text", text = p.Value }),
            },
        };

        // Quick-reply butonlarının payload'ı gönderim anında override edilir - buton metni
        // (görünen "Evet"/"Geç kalacağım"/"Hayır") Meta'da onaylı şablonun kendisinde sabit,
        // yalnızca tıklandığında webhook'a dönecek payload burada per-ders imzalanıyor.
        if (buttonPayloads is { Count: > 0 })
        {
            for (var index = 0; index < buttonPayloads.Count; index++)
            {
                components.Add(new
                {
                    type = "button",
                    sub_type = "quick_reply",
                    index = index.ToString(),
                    parameters = new object[] { new { type = "payload", payload = buttonPayloads[index] } },
                });
            }
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = "tr" },
                components,
            },
        };

        return SendAsync(payload, cancellationToken);
    }

    public Task<WhatsAppSendResult> SendAuthenticationCodeAsync(
        string toPhoneNumber, string templateName, string code, CancellationToken cancellationToken = default)
    {
        // Meta bu uzunluğu aşan kodu reddeder; çağrı yapmadan, nedeni okunur şekilde dön.
        if (code.Length > WhatsAppLimits.MaxAuthenticationCodeLength)
        {
            return Task.FromResult(new WhatsAppSendResult(
                false, null, $"Kod {WhatsAppLimits.MaxAuthenticationCodeLength} karakterden uzun; WhatsApp doğrulama şablonuyla gönderilemez."));
        }

        // Authentication şablonu sözleşmesi: kod gövdede VE "Kodu kopyala" butonunda (sub_type=url,
        // index 0) aynı değerle gönderilir.
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = "tr" },
                components = new object[]
                {
                    new { type = "body", parameters = new object[] { new { type = "text", text = code } } },
                    new { type = "button", sub_type = "url", index = "0", parameters = new object[] { new { type = "text", text = code } } },
                },
            },
        };

        return SendAsync(payload, cancellationToken);
    }

    public Task<WhatsAppSendResult> SendFreeTextAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = toPhoneNumber,
            type = "text",
            text = new { body },
        };

        return SendAsync(payload, cancellationToken);
    }

    private async Task<WhatsAppSendResult> SendAsync(object payload, CancellationToken cancellationToken)
    {
        var url = $"https://graph.facebook.com/{_options.ApiVersion}/{_options.PhoneNumberId}/messages";

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.AccessToken);

        try
        {
            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Sağlayıcı hata gövdesi telefon numarası, mesaj içeriği veya hesap ayrıntısı
                // taşıyabilir. Production loguna ham gövdeyi yazma; durum kodu operasyonel
                // teşhis ve retry kararı için yeterli, ayrıntı Meta panelinden izlenebilir.
                // Yalnızca Meta'nın sayısal hata kodu okunur (kişisel veri taşımaz) - "HTTP 404"
                // tek başına panelde neyin yanlış olduğunu söylemiyordu.
                var metaCode = await ReadMetaErrorCodeAsync(response, cancellationToken);
                logger.LogError("WhatsApp Cloud API hata döndü: {Status} (Meta kodu {MetaCode})", response.StatusCode, metaCode);
                return new WhatsAppSendResult(false, null, DescribeFailure((int)response.StatusCode, metaCode));
            }

            var result = await response.Content.ReadFromJsonAsync<CloudApiResponse>(cancellationToken: cancellationToken);
            var messageId = result?.Messages?.FirstOrDefault()?.Id;
            if (string.IsNullOrWhiteSpace(messageId))
            {
                // Meta başarılı bir gönderimde messages[0].id döndürür. Yalnızca HTTP 2xx'e
                // bakıp boş/bozuk bir cevabı başarılı sayarsak dispatcher job'ı SENT yapar ve
                // artık retry etmez; mesajın gerçekten kabul edildiğine dair elimizde hiçbir
                // sağlayıcı referansı kalmaz. Bu nedenle sözleşme eksikse fail-closed davranırız.
                logger.LogError("WhatsApp Cloud API başarılı HTTP yanıtında mesaj kimliği döndürmedi.");
                return new WhatsAppSendResult(false, null, "Sağlayıcı mesaj kimliği dönmedi.");
            }

            return new WhatsAppSendResult(true, messageId, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Uygulama kapanırken BackgroundService'in iptal sinyalini hata sonucuna çevirip
            // yutma; üst katman işlemi gerçekten durdurabilsin.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WhatsApp Cloud API çağrısı başarısız oldu.");
            return new WhatsAppSendResult(false, null, "WhatsApp sağlayıcısına ulaşılamadı.");
        }
    }

    private static async Task<int?> ReadMetaErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await System.Text.Json.JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("error", out var error) &&
                   error.ValueKind == System.Text.Json.JsonValueKind.Object &&
                   error.TryGetProperty("code", out var code) &&
                   code.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    // Sık görülen Meta kodları için ne yapılacağını söyleyen kısa açıklama; bilinmeyen kod
    // numarasıyla gösterilir, kod yoksa eski biçim ("HTTP 400") korunur.
    private static string DescribeFailure(int status, int? metaCode)
    {
        if (metaCode is null) return $"HTTP {status}";
        var hint = metaCode switch
        {
            132001 => "şablon Meta'da yok ya da Türkçe dilinde onaylı değil",
            132000 => "değişken sayısı Meta'daki şablonla uyuşmuyor",
            132012 => "değişken biçimi Meta'daki şablonla uyuşmuyor",
            132015 or 132016 => "şablon Meta tarafından durdurulmuş ya da devre dışı",
            131026 => "mesaj bu numaraya teslim edilemiyor",
            131047 => "24 saatlik yazışma penceresi kapalı",
            190 => "erişim anahtarının süresi dolmuş ya da geçersiz",
            _ => null,
        };
        return hint is null ? $"HTTP {status}, Meta kodu {metaCode}" : $"HTTP {status}, Meta kodu {metaCode}: {hint}";
    }

    private record CloudApiResponse(List<CloudApiMessage>? Messages);
    private record CloudApiMessage(string Id);
}
