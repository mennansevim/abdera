namespace Abdera.Api.Modules.Progress.Domain;

// docs/03-erd.md - Progress > lesson_notes. Progress modülünün Phase 3'te açılan tek
// dilimi - SkillDefinition/SkillAssessment/PracticeAssignment Phase 6'da geliyor
// (docs/10-decisions.md C3 ile aynı gerekçe: henüz ihtiyaç yokken açılmaz).
public class LessonNote
{
    public Guid Id { get; private set; }
    public Guid LessonId { get; private set; }
    public Guid TeacherId { get; private set; }
    public string? Practiced { get; private set; }
    public string? Note { get; private set; }
    public string? Homework { get; private set; }
    public string? NextGoal { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private LessonNote() { }

    public static LessonNote Create(
        Guid lessonId, Guid teacherId, string? practiced, string? note, string? homework, string? nextGoal, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        LessonId = lessonId,
        TeacherId = teacherId,
        Practiced = Trim(practiced),
        Note = Trim(note),
        Homework = Trim(homework),
        NextGoal = Trim(nextGoal),
        CreatedAt = now,
    };

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
