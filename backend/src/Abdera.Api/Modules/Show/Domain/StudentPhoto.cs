using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Show.Domain;

// Öğrenci fotoğrafı. Sahne ekranında "sırada kim var" sorusunu bir bakışta yanıtlamak için
// gerekiyor (kullanıcı isteği: "öğrencinin bir fotosu").
//
// İki karar:
//  1. Dosya sistemine değil VERİTABANINA yazılıyor. Bu ölçekte (~150 öğrenci, küçük
//     portreler) ayrı bir depolama katmanı açmanın karşılığı yok ve daha önemlisi: projenin
//     günlük şifreli yedeklemesi (docs/16-backup-restore.md) veritabanını kapsıyor, bir
//     volume'u kapsamıyordu. Fotoğraflar böylece hiçbir ek iş yapılmadan yedekleniyor.
//  2. `students` tablosunda değil AYRI tabloda. Öğrenci listesi neredeyse her ekranda
//     çekiliyor; blob'u aynı satıra koymak her listede yüzlerce KB taşırdı.
public class StudentPhoto
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];

    public Guid StudentId { get; private set; }
    public string ContentType { get; private set; } = null!;
    public byte[] Content { get; private set; } = null!;
    // Tarayıcı önbelleğini geçersiz kılmak için ETag olarak kullanılır - fotoğraf
    // değişmediği sürece sahne ekranı aynı baytları tekrar indirmez.
    public Guid Version { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private StudentPhoto() { }

    public static StudentPhoto Create(Guid studentId, string contentType, byte[] content, DateTimeOffset now)
    {
        var photo = new StudentPhoto { StudentId = studentId };
        photo.Replace(contentType, content, now);
        return photo;
    }

    public void Replace(string contentType, byte[] content, DateTimeOffset now)
    {
        var normalized = (contentType ?? string.Empty).Trim().ToLowerInvariant();
        if (!AllowedContentTypes.Contains(normalized))
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["file"] = ["Fotoğraf JPEG, PNG veya WebP olmalı."],
            });
        if (content.Length == 0)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["file"] = ["Dosya boş."],
            });
        if (content.Length > MaxBytes)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["file"] = ["Fotoğraf en fazla 2 MB olabilir."],
            });

        ContentType = normalized;
        Content = content;
        Version = Guid.NewGuid();
        UpdatedAt = now;
    }
}
