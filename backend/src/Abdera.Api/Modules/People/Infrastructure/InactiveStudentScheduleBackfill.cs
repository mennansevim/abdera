using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.People.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Infrastructure;

// Tek seferlik düzeltme (docs/10-decisions.md AI9). Pasife alma eskiden yalnızca durumu
// değiştiriyordu: serisi açık kalan öğrencinin dersleri takvimde duruyor, öğretmenin saati dolu
// görünüyordu. Yeni pasife almalar bunu kendisi yapar, bu tur yalnızca eski kayıtlar için.
// Kurs kayıtlarına ve paraya dokunmaz.
//
// Açılışta bir kez çalışır: bitince audit_log'a işaret satırı düşer, işaret varsa çalışmaz.
public static class InactiveStudentScheduleBackfill
{
    public const string MarkerAction = "maintenance.inactive_students_schedule_cleared";

    public static async Task RunOnceAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var scheduler = scope.ServiceProvider.GetRequiredService<INotificationScheduler>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            if (await db.AuditLogs.AnyAsync(log => log.Action == MarkerAction)) return;

            var cleared = await ClearAllAsync(db, clock, scheduler);
            db.AuditLogs.Add(AuditLog.Record(
                null, MarkerAction, nameof(Student), Guid.Empty, clock.UtcNow,
                afterJson: JsonSerializer.Serialize(new { clearedStudents = cleared })));
            await db.SaveChangesAsync();

            logger.LogInformation("Pasif öğrenci temizliği: {Count} öğrencinin programı kaldırıldı.", cleared);
        }
        catch (Exception exception)
        {
            // Düzeltme açılışı düşürmemeli; işaret yazılmadığı için bir sonraki açılışta yeniden dener.
            logger.LogError(exception, "Pasif öğrenci temizliği tamamlanamadı.");
        }
    }

    // Çağıran SaveChanges'i yapar. Dönen değer: programı temizlenen öğrenci sayısı.
    public static async Task<int> ClearAllAsync(AbderaDbContext db, IClock clock, INotificationScheduler scheduler)
    {
        var now = clock.UtcNow;
        var studentIds = await db.Students
            .Where(student => student.Status == StudentStatus.Inactive)
            .Where(student =>
                db.Lessons.Any(lesson => lesson.StudentId == student.Id && lesson.StartAt > now &&
                                         (lesson.Status == LessonStatus.Normal || lesson.Status == LessonStatus.Makeup)) ||
                db.LessonSeries.Any(series => series.Status == LessonSeriesStatus.Active &&
                                              db.Enrollments.Any(e => e.Id == series.EnrollmentId && e.StudentId == student.Id)))
            .Select(student => student.Id)
            .ToListAsync();

        foreach (var studentId in studentIds)
        {
            var removed = await StudentDeactivation.ClearScheduleAsync(studentId, db, clock, scheduler);
            db.AuditLogs.Add(AuditLog.Record(
                null, "student.schedule_cleared", nameof(Student), studentId, now,
                afterJson: JsonSerializer.Serialize(new
                {
                    RemovedLessons = removed.Select(lesson => new
                    {
                        lesson.LessonId,
                        lesson.TeacherId,
                        lesson.StartAt,
                        PreviousStatus = lesson.PreviousStatus.ToString(),
                        lesson.KeptAsCancelled,
                    }),
                })));
        }

        return studentIds.Count;
    }
}
