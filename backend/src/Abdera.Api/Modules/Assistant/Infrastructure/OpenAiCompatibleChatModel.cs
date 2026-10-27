using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Abdera.Api.Modules.Assistant.Infrastructure;

// DeepSeek (varsayılan) ya da OpenAI uyumlu bir uç. OpenAiProgressSummaryGenerator ile aynı ilke:
// fail-closed - hata, zaman aşımı ya da beklenmeyen gövdede başarısız dönülür, uydurma cevap
// gösterilmez. Sağlayıcı gövdesi anahtar/kota bilgisi içerebileceği için loglanmaz.
public class OpenAiCompatibleChatModel(HttpClient httpClient, ChatbotOptions options, ILogger<OpenAiCompatibleChatModel> logger) : IChatModel
{
    public bool IsAvailable => options.IsEnabled;

    public async Task<ChatModelReply> CompleteAsync(IReadOnlyList<JsonObject> messages, JsonArray tools, CancellationToken cancellationToken)
    {
        if (!IsAvailable) return ChatModelReply.Fail("Asistanın dil modeli yapılandırılmamış (Ai_Chatbot_ApiKey).");

        var payload = new JsonObject
        {
            ["model"] = options.Model,
            ["temperature"] = 0.2,
            ["max_tokens"] = 900,
            ["messages"] = new JsonArray(messages.Select(message => (JsonNode)message.DeepClone()).ToArray()),
            ["tools"] = tools.DeepClone(),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = new StringContent(payload.ToJsonString(Features.AssistantToolbox.Json), System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        try
        {
            using var response = await httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Asistan dil modeli hata döndürdü: {Status}", response.StatusCode);
                return ChatModelReply.Fail((int)response.StatusCode switch
                {
                    401 or 403 => "Dil modeli anahtarı geçersiz.",
                    402 => "Dil modeli hesabında bakiye yok.",
                    429 => "Dil modeli şu an çok yoğun; biraz sonra tekrar dene.",
                    _ => $"Dil modeli yanıt vermedi (HTTP {(int)response.StatusCode}).",
                });
            }

            var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (body?["choices"]?[0]?["message"] is not JsonObject message)
            {
                logger.LogError("Asistan dil modeli beklenmeyen bir yanıt döndürdü.");
                return ChatModelReply.Fail("Dil modeli beklenmeyen bir yanıt döndürdü.");
            }

            return new ChatModelReply(true, (JsonObject)message.DeepClone(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogError("Asistan dil modeli {Timeout} saniyede yanıt vermedi.", options.TimeoutSeconds);
            return ChatModelReply.Fail("Dil modeli zamanında yanıt vermedi.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Asistan dil modeli çağrısı başarısız oldu.");
            return ChatModelReply.Fail("Dil modeline ulaşılamadı.");
        }
    }
}
