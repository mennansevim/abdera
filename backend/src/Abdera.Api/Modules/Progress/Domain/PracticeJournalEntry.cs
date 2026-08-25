using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Progress.Domain;

public class PracticeJournalEntry
{
    public const int MaximumDurationMinutes = 600;

    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public DateOnly PracticeDate { get; private set; }
    public int DurationMinutes { get; private set; }
    public string Goal { get; private set; } = null!;
    public string? Note { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset? ParentApprovedAt { get; private set; }
    public Guid? ParentApprovedByGuardianId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PracticeJournalEntry() { }

    public static PracticeJournalEntry Create(
        Guid studentId, DateOnly practiceDate, int durationMinutes, string goal,
        string? note, Guid createdByUserId, DateTimeOffset now)
    {
        if (durationMinutes is < 1 or > MaximumDurationMinutes)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["durationMinutes"] = [$"Çalışma süresi 1–{MaximumDurationMinutes} dakika arasında olmalı."],
            });
        if (string.IsNullOrWhiteSpace(goal))
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["goal"] = ["Çalışma hedefi boş olamaz."],
            });
        if (goal.Trim().Length > 500 || note?.Trim().Length > 2000)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["goal"] = ["Hedef 500, not 2000 karakteri aşamaz."],
            });

        return new PracticeJournalEntry
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            PracticeDate = practiceDate,
            DurationMinutes = durationMinutes,
            Goal = goal.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
        };
    }

    public void Approve(Guid guardianId, DateTimeOffset now)
    {
        ParentApprovedByGuardianId = guardianId;
        ParentApprovedAt = now;
    }
}
