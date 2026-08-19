namespace Abdera.Api.Modules.Scheduling.Domain;

// docs/05-state-models.md - Lesson durum makinesi. RESCHEDULED/CANCELLED/COMPLETED/MAKEUP
// geçişleri Phase 3'te (attendance, lesson-change onayı) eklenecek use-case'lerin işi;
// Phase 2 yalnızca NORMAL üretimden sorumlu.
public enum LessonStatus
{
    Normal,
    Rescheduled,
    Cancelled,
    Completed,
    Makeup,
}
