using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Progress.Domain;

public class PracticeAssignment
{
    public Guid Id { get; private set; }
    public Guid LessonId { get; private set; }
    public string Description { get; private set; } = null!;
    public DateOnly? DueDate { get; private set; }
    public bool Completed { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private PracticeAssignment() { }

    public static PracticeAssignment Create(
        Guid lessonId,
        string description,
        DateOnly? dueDate,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Çalışma açıklaması boş olamaz.", nameof(description));

        return new PracticeAssignment
        {
            Id = Guid.NewGuid(),
            LessonId = lessonId,
            Description = description.Trim(),
            DueDate = dueDate,
            Completed = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void MarkCompleted(DateTimeOffset now)
    {
        if (Completed) throw new ConflictException("Bu çalışma zaten tamamlandı.");
        Completed = true;
        UpdatedAt = now;
    }
}
