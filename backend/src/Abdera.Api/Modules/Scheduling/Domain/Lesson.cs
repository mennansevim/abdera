using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Scheduling.Domain;

// docs/03-erd.md - Scheduling > lessons. LessonSeries'ten üretilen somut oturum.
// UNIQUE (lesson_series_id, start_at) kısıtı mükerrer üretimi veritabanı seviyesinde
// engeller (docs/03-erd.md) - LessonGenerator ayrıca uygulama seviyesinde de kontrol eder.
// docs/05-state-models.md - durum makinesi burada uygulanır.
public class Lesson
{
    public const int MinimumDurationMinutes = 15;
    public const int MaximumDurationMinutes = 180;
    public Guid Id { get; private set; }
    public Guid? LessonSeriesId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid TeacherId { get; private set; }
    public Guid InstrumentId { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public LessonStatus Status { get; private set; } = LessonStatus.Normal;
    public Guid? OriginalLessonId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Lesson() { }

    public static Lesson CreateFromSeries(
        Guid lessonSeriesId,
        Guid studentId,
        Guid teacherId,
        Guid instrumentId,
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        DateTimeOffset now)
    {
        if (endAt <= startAt)
            throw new ArgumentException("Bitiş zamanı başlangıçtan sonra olmalı.", nameof(endAt));

        return new Lesson
        {
            Id = Guid.NewGuid(),
            LessonSeriesId = lessonSeriesId,
            StudentId = studentId,
            TeacherId = teacherId,
            InstrumentId = instrumentId,
            StartAt = startAt,
            EndAt = endAt,
            Status = LessonStatus.Normal,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    // docs/07-api.md POST /api/makeup-credits/{creditId}/use - lesson_series_id null (ERD notu:
    // "MAKEUP dersler seriye bağlı olmayabilir") - telafi kredisi Billing/MakeupCredit tarafından
    // used_lesson_id ile bu satıya bağlanır.
    public static Lesson CreateMakeup(
        Guid studentId, Guid teacherId, Guid instrumentId,
        DateTimeOffset startAt, DateTimeOffset endAt, DateTimeOffset now)
    {
        if (endAt <= startAt)
            throw new ArgumentException("Bitiş zamanı başlangıçtan sonra olmalı.", nameof(endAt));

        return new Lesson
        {
            Id = Guid.NewGuid(),
            LessonSeriesId = null,
            StudentId = studentId,
            TeacherId = teacherId,
            InstrumentId = instrumentId,
            StartAt = startAt,
            EndAt = endAt,
            Status = LessonStatus.Makeup,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    // docs/05-state-models.md: RESCHEDULED'a geçişte orijinal referans korunur, geçmiş asla
    // üzerine yazılmaz - eski satır RESCHEDULED olarak donar, yeni satır NORMAL olarak açılır.
    public static Lesson CreateRescheduled(Lesson original, DateTimeOffset newStartAt, DateTimeOffset newEndAt, DateTimeOffset now)
    {
        if (original.Status != LessonStatus.Normal)
            throw new ConflictException("Yalnızca NORMAL durumundaki bir ders ertelenebilir.");
        if (newEndAt <= newStartAt)
            throw new ArgumentException("Bitiş zamanı başlangıçtan sonra olmalı.", nameof(newEndAt));

        original.Status = LessonStatus.Rescheduled;
        original.UpdatedAt = now;

        return new Lesson
        {
            Id = Guid.NewGuid(),
            LessonSeriesId = original.LessonSeriesId,
            StudentId = original.StudentId,
            TeacherId = original.TeacherId,
            InstrumentId = original.InstrumentId,
            StartAt = newStartAt,
            EndAt = newEndAt,
            Status = LessonStatus.Normal,
            OriginalLessonId = original.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public static Lesson CreateEditedCopy(
        Lesson original,
        Guid studentId,
        Guid teacherId,
        DateTimeOffset newStartAt,
        DateTimeOffset newEndAt,
        DateTimeOffset now)
    {
        if (original.Status != LessonStatus.Normal)
            throw new ConflictException("Yalnızca NORMAL durumundaki bir ders düzenlenebilir.");
        if (newEndAt <= newStartAt)
            throw new ArgumentException("Bitiş zamanı başlangıçtan sonra olmalı.", nameof(newEndAt));

        var durationMinutes = (newEndAt - newStartAt).TotalMinutes;
        if (durationMinutes is < MinimumDurationMinutes or > MaximumDurationMinutes)
            throw new ArgumentOutOfRangeException(nameof(newEndAt), $"Ders süresi {MinimumDurationMinutes}–{MaximumDurationMinutes} dakika arasında olmalı.");

        var participantsChanged = original.StudentId != studentId || original.TeacherId != teacherId;
        var originalSeriesId = original.LessonSeriesId;
        original.Status = LessonStatus.Rescheduled;
        original.UpdatedAt = now;
        // Yalnız süre değiştiğinde iki satırın aynı seri + başlangıç anahtarını paylaşması
        // DB tekillik kısıtını ihlal eder. Seri güncel kopyada kalır; tarihsel satır
        // OriginalLessonId zinciriyle izlenmeye devam eder.
        if (!participantsChanged) original.LessonSeriesId = null;

        return new Lesson
        {
            Id = Guid.NewGuid(),
            LessonSeriesId = participantsChanged ? null : originalSeriesId,
            StudentId = studentId,
            TeacherId = teacherId,
            InstrumentId = original.InstrumentId,
            StartAt = newStartAt,
            EndAt = newEndAt,
            Status = LessonStatus.Normal,
            OriginalLessonId = original.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    // docs/10-decisions.md A2: iptal anı ile StartAt arasında geçen süre çağıran use-case'de
    // (Attendance/Billing) ölçülür - burada yalnızca durum geçişi var, telafi kredisi kararı yok.
    public void Cancel(DateTimeOffset now)
    {
        if (Status is LessonStatus.Cancelled or LessonStatus.Completed)
            throw new ConflictException($"'{Status}' durumundaki bir ders iptal edilemez.");

        Status = LessonStatus.Cancelled;
        UpdatedAt = now;
    }

    // docs/05-state-models.md: "COMPLETED'a yalnızca LessonAttendance kaydı girildiğinde
    // geçilir" - bu yüzden Complete() yalnızca Attendance/MarkAttendance handler'ından çağrılır.
    public void Complete(DateTimeOffset now)
    {
        if (Status is LessonStatus.Cancelled)
            throw new ConflictException("İptal edilmiş bir derse yoklama girilemez.");

        Status = LessonStatus.Completed;
        UpdatedAt = now;
    }
}
