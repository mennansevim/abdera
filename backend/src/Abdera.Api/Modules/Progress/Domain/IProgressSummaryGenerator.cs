namespace Abdera.Api.Modules.Progress.Domain;

// Öğretmen ders notlarından öğrencinin genel gelişimi hakkında kısa bir yorum üretir.
//
// IWhatsAppClient/IBankPaymentProvider ile aynı desen: sağlayıcı Ai__Provider ortam
// değişkeninden seçilir, kod içinde hardcode edilmez (CLAUDE.md). Anahtar yoksa
// DisabledProgressSummaryGenerator devreye girer; gelişim ekranı yorum kutusu olmadan
// eksiksiz çalışır.
//
// Yorum yalnızca okul ekibine gösterilir - ham öğretmen notlarından türediği için veli
// portalına HİÇBİR koşulda gitmez.
public interface IProgressSummaryGenerator
{
    bool IsAvailable { get; }

    // Önbellekteki satıra hangi modelle üretildiği yazılır; model değişince eski yorumun
    // kaynağı yine de izlenebilir kalsın.
    string ModelName { get; }

    Task<ProgressSummaryResult> GenerateAsync(
        ProgressSummaryRequest request,
        CancellationToken cancellationToken = default);
}

public record ProgressSummaryNote(
    DateTimeOffset LessonStartAt,
    string InstrumentName,
    string? PieceTitle,
    int? PieceDifficulty,
    string? Practiced,
    string? Note,
    string? Homework,
    string? NextGoal);

// Notlar eskiden yeniye sıralı gelir. Sağlayıcıya öğrencinin yalnızca adı gider - soyadı,
// veli veya iletişim bilgisi gibi tanımlayıcı veri gönderilmez.
public record ProgressSummaryRequest(string? StudentFirstName, IReadOnlyList<ProgressSummaryNote> Notes);

public record ProgressSummaryResult(bool Success, string? Summary, string? Error);
