using System.Text.RegularExpressions;
using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Library.Domain;

// Okulun çalıştığı basılı nota kitaplarından tek bir eserin PDF'i. Yayıncı izni yalnızca okul
// içi kullanım için olduğundan dosya repoya girmez; veritabanında durur ve yalnızca giriş yapmış
// öğretmen/yönetici oturumuna sunulur (ScoreFiles.cs). Kitap eserlerinin katalog bilgisi frontend'deki
// school-books.json'dadır ("book-<kitap>-<no>"); kütüphaneye eklenen eserler LibraryPiece'tedir ("piece-<id>").
//
// Kitap başına değil ESER başına satır: Vercel Functions'ta istek ve yanıt gövdesi 4,5 MB ile
// sınırlı ve kitaplar 9-11 MB. Eser dosyası 1-6 sayfa, sınırın çok altında; öğretmen de
// 150 sayfalık kitabı değil yalnızca çalışacağı eseri açar. Depolama gerekçesi StudentPhoto ile aynı:
// günlük şifreli veritabanı yedeği dosyaları ek iş olmadan kapsar.
public partial class ScoreFile
{
    public const int MaxBytes = 4 * 1024 * 1024;
    public const string ContentType = "application/pdf";

    public string EntryId { get; private set; } = null!;
    public byte[] Content { get; private set; } = null!;
    public int SizeBytes { get; private set; }
    // Tarayıcıdan yüklenen dosyada bilinmez (sayfa saymak için PDF kütüphanesi gerekir); bölme
    // aracı gönderir. Yalnızca bilgi amaçlı.
    public int? PageCount { get; private set; }
    // ETag: dosya değişmediği sürece tarayıcı aynı baytları tekrar indirmez.
    public Guid Version { get; private set; }
    public Guid? UploadedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ScoreFile() { }

    public static ScoreFile Create(string entryId, byte[] content, int? pageCount, Guid? uploadedBy, DateTimeOffset now)
    {
        if (!IsValidEntryId(entryId))
            throw Invalid("entryId", "Geçersiz eser kimliği.");
        var file = new ScoreFile { EntryId = entryId, CreatedAt = now };
        file.Replace(content, pageCount, uploadedBy, now);
        return file;
    }

    public void Replace(byte[] content, int? pageCount, Guid? uploadedBy, DateTimeOffset now)
    {
        if (content.Length == 0)
            throw Invalid("file", "Dosya boş.");
        if (content.Length > MaxBytes)
            throw Invalid("file", "Eser PDF'i en fazla 4 MB olabilir.");
        // Uzantıya/Content-Type başlığına değil içeriğe bakılır: her PDF "%PDF-" ile başlar.
        if (!content.AsSpan(0, Math.Min(5, content.Length)).SequenceEqual("%PDF-"u8))
            throw Invalid("file", "Dosya bir PDF değil.");
        if (pageCount is < 1)
            throw Invalid("pageCount", "Sayfa sayısı en az 1 olmalı.");

        Content = content;
        SizeBytes = content.Length;
        PageCount = pageCount;
        Version = Guid.NewGuid();
        UploadedBy = uploadedBy;
        UpdatedAt = now;
    }

    public static bool IsBookEntry(string entryId) => entryId.StartsWith("book-", StringComparison.Ordinal);

    public static bool IsValidEntryId(string? entryId) => entryId is not null && EntryIdPattern().IsMatch(entryId);

    private static ValidationFailedException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    // book-<kitap>-<no>: basılı kitap eseri; piece-<guid>: kütüphaneye eklenen eser (LibraryPiece).
    [GeneratedRegex("^(book-[a-z0-9-]{3,90}|piece-[0-9a-f]{32})$")]
    private static partial Regex EntryIdPattern();
}
