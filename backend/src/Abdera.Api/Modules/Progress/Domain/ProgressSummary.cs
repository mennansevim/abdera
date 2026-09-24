namespace Abdera.Api.Modules.Progress.Domain;

// Gelişim ekranındaki "Genel gelişim" yorumunun önbelleği (docs/10-decisions.md C3 güncellemesi).
//
// Yorum öğretmen notlarından AI ile üretilir; her ekran açılışında sağlayıcıya gitmemek için
// son üretilen metin burada durur ve yalnızca kaynak notlar değiştiğinde yenilenir. Notlar
// silinmez/güncellenmez (LessonNotes), bu yüzden "not sayısı + en yeni notun zamanı" parmak
// izi yeni bir not girildiğini güvenle yakalar.
//
// TeacherId kapsamı: öğretmen gelişim ekranında yalnızca KENDİ notlarını görür
// (StudentProgress.ListAsync). Başka öğretmenin notlarından üretilmiş bir yorumu ona göstermek
// o notları dolaylı yoldan sızdırırdı; bu yüzden özet (öğrenci, öğretmen) başına tutulur.
// TeacherId null = tüm notlar (yönetici görünümü).
public class ProgressSummary
{
    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid? TeacherId { get; private set; }
    public string Summary { get; private set; } = null!;
    public int SourceNoteCount { get; private set; }
    public DateTimeOffset SourceLatestNoteAt { get; private set; }
    public string Model { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ProgressSummary() { }

    public static ProgressSummary Create(
        Guid studentId, Guid? teacherId, string summary, int sourceNoteCount,
        DateTimeOffset sourceLatestNoteAt, string model, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        StudentId = studentId,
        TeacherId = teacherId,
        Summary = summary,
        SourceNoteCount = sourceNoteCount,
        SourceLatestNoteAt = sourceLatestNoteAt,
        Model = model,
        CreatedAt = now,
        UpdatedAt = now,
    };

    // PostgreSQL timestamptz mikrosaniyeye yuvarlar; .NET tarafındaki değer 100ns hassasiyetinde
    // olabilir. Aynı anı temsil eden iki değer yuvarlama yüzünden "farklı" görünüp her açılışta
    // gereksiz bir AI çağrısı tetiklemesin.
    public bool IsCurrentFor(int noteCount, DateTimeOffset latestNoteAt) =>
        SourceNoteCount == noteCount &&
        Math.Abs((SourceLatestNoteAt - latestNoteAt).Ticks) < TimeSpan.TicksPerMillisecond;

    public void Refresh(string summary, int sourceNoteCount, DateTimeOffset sourceLatestNoteAt, string model, DateTimeOffset now)
    {
        Summary = summary;
        SourceNoteCount = sourceNoteCount;
        SourceLatestNoteAt = sourceLatestNoteAt;
        Model = model;
        UpdatedAt = now;
    }
}
