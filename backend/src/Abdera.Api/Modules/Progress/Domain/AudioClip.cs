using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Progress.Domain;

// Ses Defteri (issue #52): öğretmenin ders sonunda aldığı 20-60 sn'lik çalış kaydı. Ses baytları
// veritabanında DEĞİL, diskte durur (AudioClipStore); bu satır yalnızca meta veridir. Gerekçe:
// günlük tam döküm 30 gün tutuluyor, DB'ye giren her MB yedekte ~30 kat yer kaplardı.
//
// Sıkıştırma istemcide yapılır (MediaRecorder, mono Opus 32 kbps / iOS'ta AAC). Sunucu dönüştürme
// yapmaz; yalnızca boyut sınırını ve dosyanın gerçekten desteklenen bir ses kabı olduğunu denetler.
public class AudioClip
{
    // 60 sn × 32 kbps ≈ 240 KB; iOS AAC (48 kbps) ≈ 360 KB. Sınır ikisini de karşılar, süre
    // arayüzde 60 sn'de durduğu için bu sınır pratikte yalnızca kötü niyetli/bozuk istemciyi keser.
    public const int MaxBytes = 400 * 1024;
    public const int MinBytes = 1024;
    public const int MaxDurationSeconds = 60;
    public const int MaxPieceTitleLength = 200;

    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    // Öğretmen silinip öğrencileri devredilmezse kayıt öğrencinin yolculuğu olarak kalır,
    // yalnızca bu iki bağ boşalır (PersonEraser).
    public Guid? TeacherId { get; private set; }
    public Guid? LessonId { get; private set; }
    public string? PieceTitle { get; private set; }
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public int SizeBytes { get; private set; }
    public int DurationSeconds { get; private set; }
    public bool Starred { get; private set; }
    public DateTimeOffset? SharedWithGuardianAt { get; private set; }
    public Guid? SharedByUserId { get; private set; }
    // Şifreli kopyası yedek hedefine (R2) yüklendiğinde dolar; gece bakımı yalnızca boş olanları yükler.
    public DateTimeOffset? BackedUpAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool SharedWithGuardian => SharedWithGuardianAt is not null;

    private AudioClip() { }

    public static AudioClip Create(
        Guid studentId, Guid teacherId, Guid lessonId, string? pieceTitle, AudioFormat format,
        int sizeBytes, int durationSeconds, Guid createdByUserId, DateTimeOffset now)
    {
        if (sizeBytes < MinBytes)
            throw Invalid("file", "Kayıt çok kısa ya da boş.");
        if (sizeBytes > MaxBytes)
            throw Invalid("file", "Kayıt en fazla 400 KB olabilir. Kaydı 60 saniyenin altında tutun.");
        if (durationSeconds is < 1 or > MaxDurationSeconds)
            throw Invalid("durationSeconds", "Kayıt 1 ile 60 saniye arasında olmalı.");

        var title = string.IsNullOrWhiteSpace(pieceTitle) ? null : pieceTitle.Trim();
        if (title is { Length: > MaxPieceTitleLength })
            throw Invalid("pieceTitle", $"Eser adı en fazla {MaxPieceTitleLength} karakter olabilir.");

        var id = Guid.NewGuid();
        return new AudioClip
        {
            Id = id,
            StudentId = studentId,
            TeacherId = teacherId,
            LessonId = lessonId,
            PieceTitle = title,
            FileName = $"{id:N}{format.Extension}",
            ContentType = format.ContentType,
            SizeBytes = sizeBytes,
            DurationSeconds = durationSeconds,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void SetSharedWithGuardian(bool shared, Guid actorId, DateTimeOffset now)
    {
        if (shared == SharedWithGuardian) return;
        SharedWithGuardianAt = shared ? now : null;
        SharedByUserId = shared ? actorId : null;
        UpdatedAt = now;
    }

    public void SetStarred(bool starred, DateTimeOffset now)
    {
        Starred = starred;
        UpdatedAt = now;
    }

    public void MarkBackedUp(DateTimeOffset now)
    {
        BackedUpAt = now;
        UpdatedAt = now;
    }

    // Aynı eserin farklı yazımları ("Für Elise" / "für elise ") tek grup sayılır. Yalnızca
    // gruplama anahtarı olduğu için invariant küçültme yeterli (adlandırılmış kültür Alpine'de risk).
    public static string PieceKey(string? pieceTitle) =>
        string.Join(' ', (pieceTitle ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

    private static ValidationFailedException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}

// İstemcinin gönderdiği Content-Type'a değil dosyanın ilk baytlarına bakılır (ScoreFile'daki
// "%PDF-" denetimiyle aynı gerekçe). Desteklenen kaplar MediaRecorder'ın ürettikleridir.
public sealed record AudioFormat(string ContentType, string Extension)
{
    public static readonly AudioFormat WebM = new("audio/webm", ".webm");
    public static readonly AudioFormat Ogg = new("audio/ogg", ".ogg");
    public static readonly AudioFormat Mp4 = new("audio/mp4", ".m4a");

    public static AudioFormat? Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 4 && header[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 })) return WebM;
        if (header.Length >= 4 && header[..4].SequenceEqual("OggS"u8)) return Ogg;
        if (header.Length >= 8 && header[4..8].SequenceEqual("ftyp"u8)) return Mp4;
        return null;
    }
}

// Budama kuralı (issue #52): eski ara kayıtlar silinir, yolculuğu anlatanlar kalır. Saf fonksiyon,
// AudioClipMaintenance her öğrencinin kayıtlarıyla çağırır.
public static class AudioClipRetention
{
    public static IReadOnlyList<AudioClip> SelectPrunable(
        IEnumerable<AudioClip> studentClips, DateTimeOffset now, int retentionMonths)
    {
        var cutoff = now.AddMonths(-retentionMonths);
        var prunable = new List<AudioClip>();
        foreach (var piece in studentClips.GroupBy(clip => AudioClip.PieceKey(clip.PieceTitle)))
        {
            var ordered = piece.OrderBy(clip => clip.CreatedAt).ThenBy(clip => clip.Id).ToList();
            var first = ordered[0];
            var last = ordered[^1];
            prunable.AddRange(ordered.Where(clip =>
                clip.CreatedAt < cutoff
                && !clip.Starred
                && clip.Id != first.Id
                && clip.Id != last.Id));
        }
        return prunable;
    }
}
