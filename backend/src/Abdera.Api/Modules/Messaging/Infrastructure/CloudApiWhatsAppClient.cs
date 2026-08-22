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
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("WhatsApp Cloud API hata döndü: {Status} {Body}", response.StatusCode, body);
                return new WhatsAppSendResult(false, null, $"HTTP {(int)response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync<CloudApiResponse>(cancellationToken: cancellationToken);
            var messageId = result?.Messages?.FirstOrDefault()?.Id;
            return new WhatsAppSendResult(true, messageId, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "WhatsApp Cloud API çağrısı başarısız oldu.");
            return new WhatsAppSendResult(false, null, ex.Message);
        }
    }

    private record CloudApiResponse(List<CloudApiMessage>? Messages);
    private record CloudApiMessage(string Id);
}
