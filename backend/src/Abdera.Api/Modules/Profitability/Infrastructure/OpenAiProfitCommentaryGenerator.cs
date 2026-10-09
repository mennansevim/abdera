using System.Net.Http.Json;
using Abdera.Api.Modules.Profitability.Domain;
using Abdera.Api.Modules.Progress.Infrastructure;
using Microsoft.Extensions.Options;

namespace Abdera.Api.Modules.Profitability.Infrastructure;

// Ai__Provider=OpenAi. Gelişim yorumuyla aynı Ai__* yapılandırması ve aynı fail-closed ilke
// (OpenAiProgressSummaryGenerator): hata, zaman aşımı ya da boş yanıtta BAŞARISIZ döner,
// yarım/uydurma metin gösterilmez. Girdi ProfitCommentaryFacts'in isimsiz toplu sayılarıdır.
public class OpenAiProfitCommentaryGenerator(
    HttpClient httpClient,
    IOptions<AiOptions> options,
    ILogger<OpenAiProfitCommentaryGenerator> logger) : IProfitCommentaryGenerator
{
    // Çıktı tavanı: 4-6 cümlelik Türkçe yorum ~250 token; pay bırakılarak sınırlandı.
    public const int MaxOutputTokens = 400;
    // Olgu metni normalde ~4-6 bin karakter; beklenmedik büyümede maliyet sınırlı kalsın.
    public const int MaxFactsLength = 8000;

    private readonly AiOptions _options = options.Value;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public string ModelName => _options.Model;

    public const string SystemPrompt =
        "Sen küçük bir müzik okulunun yöneticisine aylık kârlılık danışmanlığı yapıyorsun. Kurallar: " +
        "(1) Yalnızca Türkçe, düz metin; başlık, madde işareti, emoji yok. Yöneticiye 'sen' diye hitap et. " +
        "Birinci çoğul şahıs YASAK: 'biz', '-acağız/-eceğiz', '-ebiliriz', '-alım/-elim' kullanma; öneriyi emir kipiyle yaz " +
        "('öğrenci bul', 'öğretmen ücretlerini gir'). Örnek üslup: 'Ekim'de cebinde ₺42.000 kaldı, geçen aydan ₺3.000 fazla. " +
        "Piyano öğretmenlerinin 30 saati boş; yeni öğretmen alma, bu saatlere öğrenci bul.' " +
        "(2) 4-6 kısa cümle, en fazla 110 kelime. Sade, günlük dille yaz: marj, katkı, doluluk oranı, kaldıraç gibi " +
        "finans/teknik terim KULLANMA; 'cebinde kalan', 'boş saat', 'ayda ₺X kazandırır' gibi herkesin anlayacağı ifadeler kullan. " +
        "(3) Sıra: bu ay cebinde kalan para ve geçen aya göre arttı mı azaldı mı (rakamla); en büyük kârlılık fırsatı; " +
        "işleyişteki en önemli eksik ve kâr hesabına etkisi; son cümlede bu ay yapılacak TEK somut iş. " +
        "(4) Olgularda 'Yöneticinin fikirleri' bölümü varsa en az birini adıyla değerlendir (önerilen olanı destekle, " +
        "önerilmeyenin nedenini söyle); bu bölüm yoksa fikirlerden hiç bahsetme. " +
        "(5) Yalnızca verilen rakamları kullan; yeni rakam, oran veya olay UYDURMA, hesap ya da kıyas yapma. " +
        "Artış/azalış yönünü ÖZET satırından al; ÖNEMLİ satırı varsa içindeki cümleyi kelimesi kelimesine aktar. " +
        "(6) Tahsilat ve ödeme takibine odaklanma: veliler düzenli ödüyor, soru kârlılık. " +
        "(7) Öğretmen ücretleri girilmemiş ya da veri az ise sonucun bu yüzden belirsiz olduğunu açıkça söyle.";

    public async Task<ProfitCommentaryResult> GenerateAsync(string facts, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return new ProfitCommentaryResult(false, null, "AI sağlayıcısı için Ai__ApiKey tanımlanmamış.");

        var payload = new
        {
            model = _options.Model,
            temperature = 0.2,
            max_tokens = MaxOutputTokens,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = facts.Length > MaxFactsLength ? facts[..MaxFactsLength] : facts },
            },
        };

        var url = $"{_options.BaseUrl.TrimEnd('/')}/chat/completions";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            var response = await httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                // Gövde anahtar/kota bilgisi içerebilir: yalnızca durum kodu loglanır.
                logger.LogError("AI sağlayıcısı hata döndürdü: {Status}", response.StatusCode);
                return new ProfitCommentaryResult(false, null, $"AI sağlayıcısı yanıt vermedi (HTTP {(int)response.StatusCode}).");
            }

            var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: timeout.Token);
            var text = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            return string.IsNullOrWhiteSpace(text)
                ? new ProfitCommentaryResult(false, null, "AI sağlayıcısı boş bir yorum döndürdü.")
                : new ProfitCommentaryResult(true, text, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogError("AI sağlayıcısı {Timeout} saniyede yanıt vermedi.", _options.TimeoutSeconds);
            return new ProfitCommentaryResult(false, null, "AI sağlayıcısı zamanında yanıt vermedi.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI sağlayıcısı çağrısı başarısız oldu.");
            return new ProfitCommentaryResult(false, null, "AI sağlayıcısına ulaşılamadı.");
        }
    }

    private record ChatCompletionResponse(List<ChatChoice>? Choices);
    private record ChatChoice(ChatMessage? Message);
    private record ChatMessage(string? Content);
}
