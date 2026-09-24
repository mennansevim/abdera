using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using Abdera.Api.Modules.Progress.Domain;
using Microsoft.Extensions.Options;

namespace Abdera.Api.Modules.Progress.Infrastructure;

// Ai__Provider=OpenAi. OpenAI uyumlu /chat/completions çağrısı.
//
// Hata yönetimi CloudApiWhatsAppClient ile aynı ilkede: fail-closed. Sağlayıcı hata verir,
// zaman aşımına uğrar veya beklenen sözleşmeyi döndürmezse BAŞARISIZ döneriz - asla yarım
// ya da uydurma bir metin "gelişim yorumu" diye gösterilmez.
public class OpenAiProgressSummaryGenerator(
    HttpClient httpClient,
    IOptions<AiOptions> options,
    ILogger<OpenAiProgressSummaryGenerator> logger) : IProgressSummaryGenerator
{
    // Uzun süredir ders alan bir öğrencide tüm geçmişi göndermek maliyeti ve gecikmeyi
    // büyütür; genel gidişatı son dönem notları zaten taşıyor.
    public const int MaxNotes = 30;
    private const int MaxFieldLength = 600;

    private readonly AiOptions _options = options.Value;

    // Anahtar boşsa sağlayıcı seçilmiş olsa bile özellik kullanılamaz (ProductionSecretsGuard
    // da Production'da bu durumu reddeder).
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public string ModelName => _options.Model;

    private const string SystemPrompt =
        "Sen bir müzik okulunda öğretmenlerin ders notlarını okuyup öğrencinin genel gelişimini " +
        "okul ekibi için özetleyen bir yardımcısın. Kurallar: " +
        "(1) Yalnızca Türkçe yaz. " +
        "(2) 3-4 cümle, en fazla 90 kelime, tek paragraf. " +
        "(3) Notlar eskiden yeniye sıralı: zaman içindeki değişimi (ilerleme, tekrar eden zorluk, " +
        "istikrar) anlat; tek bir dersi özetleme. " +
        "(4) Güçlü yanı ve üzerinde durulması gereken konuyu belirt, son cümlede somut bir sonraki odak öner. " +
        "(5) Yalnızca notlardaki bilgiye dayan; notta olmayan başarı, ölçüm veya olay UYDURMA. Not azsa bunu söyle. " +
        "(6) Teşhis koyma, öğrenciyi etiketleme, başka öğrencilerle kıyaslama. " +
        "(7) Yalnızca yorum metnini döndür - başlık, madde işareti, tırnak veya açıklama ekleme.";

    public async Task<ProgressSummaryResult> GenerateAsync(
        ProgressSummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
            return new ProgressSummaryResult(false, null, "AI sağlayıcısı için Ai__ApiKey tanımlanmamış.");
        if (request.Notes.Count == 0)
            return new ProgressSummaryResult(false, null, "Yorumlanacak ders notu yok.");

        var payload = new
        {
            model = _options.Model,
            temperature = 0.3,
            max_tokens = 350,
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = BuildUserMessage(request) },
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
                return new ProgressSummaryResult(false, null, $"AI sağlayıcısı yanıt vermedi (HTTP {(int)response.StatusCode}).");
            }

            var result = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: timeout.Token);
            var summary = result?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            if (string.IsNullOrWhiteSpace(summary))
            {
                logger.LogError("AI sağlayıcısı başarılı yanıtta boş içerik döndürdü.");
                return new ProgressSummaryResult(false, null, "AI sağlayıcısı boş bir yorum döndürdü.");
            }

            return new ProgressSummaryResult(true, summary, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // İstek gerçekten iptal edildiyse (istemci bağlantıyı kapattı) yutma, ilet.
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogError("AI sağlayıcısı {Timeout} saniyede yanıt vermedi.", _options.TimeoutSeconds);
            return new ProgressSummaryResult(false, null, "AI sağlayıcısı zamanında yanıt vermedi.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI sağlayıcısı çağrısı başarısız oldu.");
            return new ProgressSummaryResult(false, null, "AI sağlayıcısına ulaşılamadı.");
        }
    }

    // Tarihler kültürden bağımsız ISO biçiminde yazılır (CLAUDE.md: Alpine imajında adlandırılmış
    // kültür yoktur; model için de ISO tarih en az belirsiz olanı).
    public static string BuildUserMessage(ProgressSummaryRequest request)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.StudentFirstName))
            builder.Append("Öğrencinin adı: ").AppendLine(request.StudentFirstName);
        builder.Append("Ders notları (eskiden yeniye, ").Append(request.Notes.Count).AppendLine(" kayıt):");

        foreach (var note in request.Notes.TakeLast(MaxNotes))
        {
            builder.AppendLine();
            builder.Append("- ").Append(note.LessonStartAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append(" · ").AppendLine(note.InstrumentName);
            AppendField(builder, "Eser", note.PieceTitle is null
                ? null
                : note.PieceDifficulty is { } difficulty ? $"{note.PieceTitle} (zorluk {difficulty}/5)" : note.PieceTitle);
            AppendField(builder, "Çalışılan", note.Practiced);
            AppendField(builder, "Öğretmen notu", note.Note);
            AppendField(builder, "Ödev", note.Homework);
            AppendField(builder, "Sonraki hedef", note.NextGoal);
        }

        return builder.ToString();
    }

    private static void AppendField(StringBuilder builder, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var trimmed = value.Trim();
        if (trimmed.Length > MaxFieldLength) trimmed = trimmed[..MaxFieldLength] + "…";
        builder.Append("  ").Append(label).Append(": ").AppendLine(trimmed.ReplaceLineEndings(" "));
    }

    private record ChatCompletionResponse(List<ChatChoice>? Choices);
    private record ChatChoice(ChatMessage? Message);
    private record ChatMessage(string? Content);
}
