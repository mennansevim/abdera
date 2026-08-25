namespace Abdera.Api.Modules.Progress.Domain;

public class SkillAssessment
{
    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid SkillDefinitionId { get; private set; }
    public Guid TeacherId { get; private set; }
    public Guid? LessonId { get; private set; }
    public int Score { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset AssessedAt { get; private set; }

    private SkillAssessment() { }

    public static SkillAssessment Create(
        Guid studentId,
        Guid skillDefinitionId,
        Guid teacherId,
        Guid? lessonId,
        int score,
        string? note,
        DateTimeOffset assessedAt)
    {
        if (score is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(score), "Yetenek puanı 1 ile 5 arasında olmalı.");

        return new SkillAssessment
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            SkillDefinitionId = skillDefinitionId,
            TeacherId = teacherId,
            LessonId = lessonId,
            Score = score,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            AssessedAt = assessedAt,
        };
    }
}
