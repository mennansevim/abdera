namespace Abdera.Api.Modules.People.Domain;

public enum EnrollmentStatus
{
    Active,
    Paused,
    Ended,
}

// docs/03-erd.md - People > enrollments. Bir öğrencinin belirli bir öğretmenle belirli
// bir enstrüman üzerindeki kaydı - LessonSeries ve FeePlan bunun üzerine kurulur.
public class Enrollment
{
    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid TeacherId { get; private set; }
    public Guid InstrumentId { get; private set; }
    public EnrollmentStatus Status { get; private set; } = EnrollmentStatus.Active;
    public DateOnly StartedAt { get; private set; }
    public DateOnly? EndedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Enrollment() { }

    public static Enrollment Create(
        Guid studentId, Guid teacherId, Guid instrumentId, DateOnly startedAt, DateTimeOffset now)
    {
        // İstek gövdesinde startedAt eksikse System.Text.Json onu sessizce default(DateOnly)
        // (0001-01-01) yapıyordu, hiç hata vermeden - canlı QA turunda bulunan gerçek bir bug.
        if (startedAt == default) throw new ArgumentException("Başlangıç tarihi zorunlu.", nameof(startedAt));

        return new Enrollment
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            TeacherId = teacherId,
            InstrumentId = instrumentId,
            Status = EnrollmentStatus.Active,
            StartedAt = startedAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void End(DateOnly endedAt, DateTimeOffset now)
    {
        Status = EnrollmentStatus.Ended;
        EndedAt = endedAt;
        UpdatedAt = now;
    }

    public void SetStatus(EnrollmentStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
    }
}
