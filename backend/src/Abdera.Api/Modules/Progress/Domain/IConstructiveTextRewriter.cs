namespace Abdera.Api.Modules.Progress.Domain;

// Faz 10: öğretmenin kısa/ham ders notunu veliye gösterilebilecek yapıcı bir metne çevirir.
//
// IWhatsAppClient/IBankPaymentProvider ile aynı desen: sağlayıcı Ai__Provider ortam
// değişkeninden seçilir, kod içinde hardcode edilmez (CLAUDE.md). Anahtar yoksa
// DisabledConstructiveTextRewriter devreye girer ve özellik kapalı kalır - manuel yorum
// yazma/onaylama akışı bundan HİÇ etkilenmez.
//
// Önemli sınır: bu arayüz yalnızca ÖNERİ üretir. Öneriyi kaydetmek, düzenlemek ve veliye
// açmak öğretmenin açık eylemidir (LessonNote.SetParentCommentDraft/ApproveParentComment) -
// AI çıktısı hiçbir koşulda doğrudan veliye gitmez.
public interface IConstructiveTextRewriter
{
    bool IsAvailable { get; }

    Task<ConstructiveRewriteResult> RewriteAsync(
        ConstructiveRewriteRequest request,
        CancellationToken cancellationToken = default);
}

// StudentFirstName ve PieceTitle yalnızca metnin doğal okunması için verilir; ikisi de
// opsiyoneldir ve sağlayıcıya öğrenci kimliği/veli bilgisi gibi tanımlayıcı veri gönderilmez.
public record ConstructiveRewriteRequest(string RawNote, string? StudentFirstName, string? PieceTitle);

public record ConstructiveRewriteResult(bool Success, string? Suggestion, string? Error);
