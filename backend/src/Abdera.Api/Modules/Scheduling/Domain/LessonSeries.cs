namespace Abdera.Api.Modules.Scheduling.Domain;

// docs/03-erd.md - Scheduling > lesson_series. "Her Salı 18:00" gibi tekrarlayan programı
// temsil eder; somut Lesson occurrence'ları buradan üretilir (LessonGenerator).
public class LessonSeries
{
    public Guid Id { get; private set; }
    public Guid EnrollmentId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public int DurationMinutes { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveUntil { get; private set; }
    public LessonSeriesStatus Status { get; private set; } = LessonSeriesStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private LessonSeries() { }

    public static LessonSeries Create(
        Guid enrollmentId,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        int durationMinutes,
        DateOnly effectiveFrom,
        DateOnly? effectiveUntil,
        DateTimeOffset now)
    {
        if (durationMinutes <= 0)
            throw new ArgumentException("Ders süresi pozitif olmalı.", nameof(durationMinutes));
        if (effectiveUntil is { } until && until < effectiveFrom)
            throw new ArgumentException("Bitiş tarihi başlangıçtan önce olamaz.", nameof(effectiveUntil));

        return new LessonSeries
        {
            Id = Guid.NewGuid(),
            EnrollmentId = enrollmentId,
            DayOfWeek = dayOfWeek,
            StartTime = startTime,
            DurationMinutes = durationMinutes,
            EffectiveFrom = effectiveFrom,
            EffectiveUntil = effectiveUntil,
            Status = LessonSeriesStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public TimeOnly EndTime => StartTime.AddMinutes(DurationMinutes);

    // Yönetici seriyi belirli bir tarihte sonlandırır - geçmiş dersler etkilenmez,
    // yalnızca gelecekteki üretim durur (CLAUDE.md - audit-friendly history).
    public void EndAs(DateOnly effectiveUntil, DateTimeOffset now)
    {
        if (effectiveUntil < EffectiveFrom)
            throw new ArgumentException("Bitiş tarihi başlangıçtan önce olamaz.", nameof(effectiveUntil));

        EffectiveUntil = effectiveUntil;
        Status = LessonSeriesStatus.Ended;
        UpdatedAt = now;
    }
}
