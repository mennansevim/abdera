using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Attendance.Infrastructure;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Attendance.Features;

// Kullanıcı isteği: "yorum gelişme notu girerse otomatik olarak geldi işaretle." Öğretmen bir
// derse not yazdıysa öğrenci o derse gelmiştir; ayrıca Geldi'ye basmasını beklemek hem gereksiz
// adım hem de zilde cevapsız kalan bir soru demek.
//
// Yalnızca yoklama HİÇ girilmemişse işler: öğretmen önce "gelmedi" dediyse not onu ezmez
// (gelmeyen öğrenci için de not yazılabilir - ör. "ödevini veliye ilettim").
public static class AttendanceFromNote
{
    /// <returns>Yoklama otomatik "geldi" olarak girildiyse true.</returns>
    // SaveChanges çağırmaz: not ile yoklama çağıranın tek SaveChanges'inde birlikte yazılır.
    public static async Task<bool> MarkPresentIfUnmarkedAsync(
        AbderaDbContext db, IClock clock, IStaffNotifier notifier, Lesson lesson, Guid actorUserId)
    {
        // İptal/ertelenmiş derse yoklama girilmez; henüz başlamamış derse yazılan not (hazırlık
        // notu) öğrencinin geldiğini göstermez.
        if (lesson.Status is not (LessonStatus.Normal or LessonStatus.Makeup)) return false;
        var now = clock.UtcNow;
        if (lesson.StartAt > now) return false;
        if (await db.LessonAttendances.AnyAsync(attendance => attendance.LessonId == lesson.Id)) return false;

        var attendance = LessonAttendance.Create(lesson.Id, AttendanceStatus.Present, lesson.TeacherId, null, now);
        db.LessonAttendances.Add(attendance);
        lesson.Complete(now);
        db.AuditLogs.Add(AuditLog.Record(
            actorUserId, "lesson.attendance_marked_from_note", nameof(LessonAttendance), attendance.Id, now));
        await notifier.ClearReminderForAllAsync(
            StaffNotificationType.AttendanceMissing, AttendanceReminderJob.ReferenceType, lesson.Id);
        return true;
    }
}
