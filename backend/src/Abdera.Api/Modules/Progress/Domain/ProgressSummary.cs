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
//
// Yenileme politikası (kullanıcı isteği: "ilk kez 4 yorum girildikten sonra takip eden her ay
// yapay zekâ yorumu alınsın, zaten varsa önbellekten gösterilsin"): eskiden HER yeni notta
// yeniden üretiliyordu. Artık Decide() tek karar noktası:
//   - 4 nottan azsa ve kayıtlı yorum yoksa üretilmez (NotEnoughNotes).
//   - Kayıtlı yorum aynı notları kapsıyorsa ya da bu ay (okulun yerel takvimi) üretildiyse
//     olduğu gibi gösterilir; yeni notlar bir sonraki ay yoruma girer.
//   - Ay değişmiş ve yeni not varsa bir kez üretilir.
public enum ProgressSummaryDecision { NotEnoughNotes, ServeCached, Generate }

public class ProgressSummary
{
    public const int MinimumNotes = 4;

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

    public static ProgressSummaryDecision Decide(
        ProgressSummary? cached, int noteCount, DateTimeOffset latestNoteAt,
        DateTimeOffset now, Func<DateTimeOffset, DateTimeOffset> toSchoolLocal)
    {
        if (cached is null)
            return noteCount >= MinimumNotes ? ProgressSummaryDecision.Generate : ProgressSummaryDecision.NotEnoughNotes;
        if (cached.IsCurrentFor(noteCount, latestNoteAt))
            return ProgressSummaryDecision.ServeCached;

        var generated = toSchoolLocal(cached.UpdatedAt);
        var today = toSchoolLocal(now);
        var sameMonth = generated.Year == today.Year && generated.Month == today.Month;
        return sameMonth ? ProgressSummaryDecision.ServeCached : ProgressSummaryDecision.Generate;
    }

    // Kayıtlı yorumun en erken yenilenebileceği gün: üretildiği ayı izleyen ayın 1'i (yerel).
    public DateOnly NextRefreshOn(Func<DateTimeOffset, DateTimeOffset> toSchoolLocal)
    {
        var generated = toSchoolLocal(UpdatedAt);
        return new DateOnly(generated.Year, generated.Month, 1).AddMonths(1);
    }

    public void Refresh(string summary, int sourceNoteCount, DateTimeOffset sourceLatestNoteAt, string model, DateTimeOffset now)
    {
        Summary = summary;
        SourceNoteCount = sourceNoteCount;
        SourceLatestNoteAt = sourceLatestNoteAt;
        Model = model;
        UpdatedAt = now;
    }
}
