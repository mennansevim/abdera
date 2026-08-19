namespace Abdera.Api.Modules.Attendance.Domain;

public enum AttendanceStatus
{
    Present,
    Absent,
    Excused,
}

// docs/03-erd.md - Attendance > lesson_attendances. UNIQUE (lesson_id) - tek yönlü kayıt,
// düzeltme gerekirse mevcut satır güncellenir (docs/05-state-models.md), yeni satır açılmaz.
public class LessonAttendance
{
    public Guid Id { get; private set; }
    public Guid LessonId { get; private set; }
    public AttendanceStatus Status { get; private set; }
    public Guid MarkedByTeacherId { get; private set; }
    public DateTimeOffset MarkedAt { get; private set; }
    public string? Note { get; private set; }

    private LessonAttendance() { }

    public static LessonAttendance Create(Guid lessonId, AttendanceStatus status, Guid markedByTeacherId, string? note, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        LessonId = lessonId,
        Status = status,
        MarkedByTeacherId = markedByTeacherId,
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        MarkedAt = now,
    };

    // docs/05-state-models.md: "düzeltme gerekirse mevcut kayıt güncellenir ve audit_log'a
    // düşer" - audit yazma sorumluluğu çağıran handler'da (MarkAttendance.cs).
    public void Correct(AttendanceStatus status, Guid markedByTeacherId, string? note, DateTimeOffset now)
    {
        Status = status;
        MarkedByTeacherId = markedByTeacherId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        MarkedAt = now;
    }
}
