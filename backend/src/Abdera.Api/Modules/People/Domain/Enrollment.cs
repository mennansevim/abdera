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
