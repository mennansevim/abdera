namespace Abdera.Api.Modules.Scheduling.Domain;

// docs/03-erd.md - Scheduling > lessons. LessonSeries'ten üretilen somut oturum.
// UNIQUE (lesson_series_id, start_at) kısıtı mükerrer üretimi veritabanı seviyesinde
// engeller (docs/03-erd.md) - LessonGenerator ayrıca uygulama seviyesinde de kontrol eder.
public class Lesson
{
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
}
