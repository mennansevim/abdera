using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Modules.Scheduling.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// Öğrenciyi pasife almak (docs/10-decisions.md AI): aktif kurs kayıtları bitirilir, gelecekteki
// planlı dersleri takvimden ve öğretmen programından kalkar. Geçmiş dersler, yoklamalar, aidat
// ve ödemeler değişmez; yeniden aktif etmek dersleri geri getirmez. Grup dersinde her üyenin
// kendi ders satırı olduğu için yalnızca bu öğrenci çıkar, sınıf devam eder.
public static class StudentDeactivation
{
    public record ImpactResponse(
        int FutureLessons, int ActiveEnrollments, IReadOnlyList<string> Teachers, bool RequiresAdmin);

    internal static async Task<ImpactResponse> DescribeAsync(Guid studentId, Guid? teacherScope, AbderaDbContext db, IClock clock)
    {
        var now = clock.UtcNow;
        var teacherIds = await FutureLessons(studentId, now, db).Select(lesson => lesson.TeacherId)
            .Concat(ActiveEnrollments(studentId, db).Select(enrollment => enrollment.TeacherId))
            .Distinct()
            .ToListAsync();
        var requiresAdmin = teacherScope is { } scoped && teacherIds.Any(id => id != scoped);

        return new ImpactResponse(
            await FutureLessons(studentId, now, db).CountAsync(),
            await ActiveEnrollments(studentId, db).CountAsync(),
            // Öğretmen oturumu başka öğretmenlerin adını görmez (docs/04-permissions.md).
            teacherScope is null
                ? await db.Teachers.Where(teacher => teacherIds.Contains(teacher.Id))
                    .OrderBy(teacher => teacher.FirstName).ThenBy(teacher => teacher.LastName)
                    .Select(teacher => teacher.FirstName + " " + teacher.LastName)
                    .ToListAsync()
                : [],
            requiresAdmin);
    }

    // Kurs kaydı bitirmek öğretmende değil (J1); öğretmen yalnızca dersleri tümüyle kendisinde
    // olan öğrenciyi pasife alabilir, başka öğretmenin programına dokunamaz.
    internal static async Task EnsureAllowedAsync(Guid studentId, Guid? teacherScope, AbderaDbContext db, IClock clock)
    {
        if (teacherScope is null) return;
        if ((await DescribeAsync(studentId, teacherScope, db, clock)).RequiresAdmin)
            throw new ForbiddenException("Bu öğrencinin başka öğretmenle de dersi var; pasife almayı yönetici yapabilir.");
    }

    // Değişiklikleri izler, çağıran tek SaveChanges ile kaydeder; e-postalar ondan sonra gider.
    internal static async Task ApplyAsync(
        Student student, EnrollmentEndReason? reason, string? note, Guid? actorUserId,
        AbderaDbContext db, IClock clock, INotificationScheduler scheduler, IStaffNotifier staffNotifier)
    {
        var now = clock.UtcNow;
        var enrollments = await ActiveEnrollments(student.Id, db).ToListAsync();
        foreach (var enrollment in enrollments)
        {
            await Enrollments.EndAndCloseSeriesAsync(enrollment, reason, note, actorUserId, db, clock, scheduler);
        }

        var lessons = await FutureLessons(student.Id, now, db).OrderBy(lesson => lesson.StartAt).ToListAsync();
        var removed = await FutureLessonRemoval.RemoveAsync(lessons, db, now);

        db.AuditLogs.Add(AuditLog.Record(
            actorUserId,
            "student.deactivated",
            nameof(Student),
            student.Id,
            now,
            JsonSerializer.Serialize(new { Status = StudentStatus.Active.ToString() }),
            JsonSerializer.Serialize(new
            {
                Status = student.Status.ToString(),
                EndedEnrollmentIds = enrollments.Select(enrollment => enrollment.Id),
                EndReason = reason?.ToString(),
                EndNote = note,
                RemovedLessons = removed.Select(lesson => new
                {
                    lesson.LessonId,
                    lesson.TeacherId,
                    lesson.StartAt,
                    PreviousStatus = lesson.PreviousStatus.ToString(),
                    lesson.KeptAsCancelled,
                }),
            })));

        // Öğretmen başına tek özet: her ders için ayrı bildirim 10 haftalık programda zili ve
        // gelen kutusunu doldururdu (M4).
        foreach (var group in removed.GroupBy(lesson => lesson.TeacherId))
        {
            await LessonChangeNotice.NotifyStudentLessonsRemovedAsync(
                staffNotifier, db, actorUserId, group.Key, student.Id, group.Count(), group.First().LessonId);
        }
    }

    private static IQueryable<Enrollment> ActiveEnrollments(Guid studentId, AbderaDbContext db) =>
        db.Enrollments.Where(enrollment => enrollment.StudentId == studentId && enrollment.Status != EnrollmentStatus.Ended);

    private static IQueryable<Lesson> FutureLessons(Guid studentId, DateTimeOffset now, AbderaDbContext db) =>
        db.Lessons.Where(lesson => lesson.StudentId == studentId && lesson.StartAt > now &&
                                   (lesson.Status == LessonStatus.Normal || lesson.Status == LessonStatus.Makeup));
}
