using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Domain;

// Tekil ders çakışma kontrolü - ChangeRequests (reschedule) ve MakeupCredits (telafi dersi
// planlama) tarafından paylaşılır (CLAUDE.md: duplicated business rules yok). LessonSeries
// oluştururken kullanılan seri-bazlı çakışma kontrolünden (LessonSeriesFeatures) farklıdır -
// o gelecekteki tüm occurrence'ları, bu yalnızca tek bir zaman aralığını kontrol eder.
public static class LessonConflictChecker
{
    public static Task<bool> HasOverlapAsync(
        AbderaDbContext db, Guid teacherId, Guid studentId,
        DateTimeOffset start, DateTimeOffset end, Guid? excludeLessonId = null)
    {
        var query = db.Lessons
            .Where(l => l.Status != LessonStatus.Cancelled && l.Status != LessonStatus.Rescheduled)
            .Where(l => l.TeacherId == teacherId || l.StudentId == studentId)
            .Where(l => l.StartAt < end && start < l.EndAt);

        if (excludeLessonId is { } id)
        {
            query = query.Where(l => l.Id != id);
        }

        return query.AnyAsync();
    }
}
