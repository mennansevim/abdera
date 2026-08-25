using System.Net.Http.Json;
using Abdera.Api.Modules.Progress.Domain;
using Microsoft.Extensions.Options;

namespace Abdera.Api.Modules.Progress.Infrastructure;

public class AiOptions
{
    public string Provider { get; set; } = "Disabled"; // Disabled | OpenAi
    public string ApiKey { get; set; } = "";

    // OpenAI uyumlu herhangi bir uç nokta kullanılabilir (OpenAI, Azure OpenAI, kendi
    // gateway'in) - bu yüzden adres sabit değil, konfigürasyondan gelir.
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-4o-mini";

    // Öğretmen bir düğmeye basıp bekliyor; sağlayıcı yavaşsa süresiz asılı kalmasın.
    public int TimeoutSeconds { get; set; } = 20;
}

// Ai__Provider=OpenAi. OpenAI uyumlu /chat/completions çağrısı.
//
// Hata yönetimi CloudApiWhatsAppClient ile aynı ilkede: fail-closed. Sağlayıcı hata verir,
// zaman aşımına uğrar veya beklenen sözleşmeyi döndürmezse BAŞARISIZ döneriz ve öğretmen
// notunu elle yazmaya devam eder - asla yarım/uydurma bir metin "öneri" diye sunulmaz.
public class OpenAiConstructiveTextRewriter(
    HttpClient httpClient,
    IOptions<AiOptions> options,
    ILogger<OpenAiConstructiveTextRewriter> logger) : IConstructiveTextRewriter
{
    private readonly AiOptions _options = options.Value;

    // Anahtar boşsa sağlayıcı seçilmiş olsa bile özellik kullanılamaz; UI bunu bayraktan
    // görüp butonu kapatır (ProductionSecretsGuard da Production'da bu durumu reddeder).
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_options.ApiKey);

    private const string SystemPrompt =
        "Sen bir müzik okulunda öğretmenlerin kısa ders notlarını velilere iletilecek " +
        "yapıcı bir yoruma dönüştüren bir yardımcısın. Kurallar: " +
        "(1) Yalnızca Türkçe yaz. " +
        "(2) 2-3 cümle, en fazla 60 kelime. " +
        "(3) Öğretmenin gözlemine sadık kal; notta olmayan bir başarı, ölçüm veya olay UYDURMA. " +
        "(4) Olumsuz gözlemi yok sayma; somut ve yapıcı bir sonraki adım olarak ifade et. " +
        "(5) Teşhis koyma, öğrenciyi etiketleme, başka öğrencilerle kıyaslama. " +
        "(6) Yalnızca yorum metnini döndür - başlık, tırnak veya açıklama ekleme.";

    public async Task<ConstructiveRewriteResult> RewriteAsync(
        ConstructiveRewriteRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return new ConstructiveRewriteResult(false, null, "AI sağlayıcısı için Ai__ApiKey tanımlanmamış.");
        }

        var context = new List<string> { $"Öğretmenin ham notu: {request.RawNote}" };
        if (!string.IsNullOrWhiteSpace(request.StudentFirstName))
            context.Add($"Öğrencinin adı: {request.StudentFirstName}");
        if (!string.IsNullOrWhiteSpace(request.PieceTitle))
            context.Add($"Çalışılan eser: {request.PieceTitle}");

        var payload = new
        {
            model = _options.Model,
            temperature = 0.4,
            max_tokens = 300,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = string.Join("\n", context) },
            },
        };

        var url = $"{_options.BaseUrl.TrimEnd('/')}/chat/completions";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var response = await httpClient.SendAsync(httpRequest, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                // Sağlayıcı gövdesi anahtar/kota bilgisi içerebilir - yalnızca durum kodunu
                // logla, kullanıcıya da ham gövdeyi gösterme (CLAUDE.md "safe logging").
                logger.LogError("AI sağlayıcısı hata döndürdü: {Status}", response.StatusCode);
                return new ConstructiveRewriteResult(false, null, $"AI sağlayıcısı yanıt vermedi (HTTP {(int)response.StatusCode}).");
            }

            var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: timeout.Token);
            var suggestion = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            if (string.IsNullOrWhiteSpace(suggestion))
            {
                logger.LogError("AI sağlayıcısı başarılı yanıtta boş içerik döndürdü.");
                return new ConstructiveRewriteResult(false, null, "AI sağlayıcısı boş bir öneri döndürdü.");
            }

            return new ConstructiveRewriteResult(true, suggestion, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // İstek gerçekten iptal edildiyse (istemci bağlantıyı kapattı) yut değil, ilet.
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogError("AI sağlayıcısı {Timeout} saniyede yanıt vermedi.", _options.TimeoutSeconds);
            return new ConstructiveRewriteResult(false, null, "AI sağlayıcısı zamanında yanıt vermedi.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI sağlayıcısı çağrısı başarısız oldu.");
            return new ConstructiveRewriteResult(false, null, "AI sağlayıcısına ulaşılamadı.");
        }
    }

    private record ChatCompletionResponse(List<ChatChoice>? Choices);
    private record ChatChoice(ChatMessage? Message);
    private record ChatMessage(string? Content);
}
