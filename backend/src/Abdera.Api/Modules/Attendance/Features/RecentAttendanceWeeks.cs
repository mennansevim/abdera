using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Attendance.Features;

// Öğrenci listesindeki "Son 4 hafta" rozetlerinin veri kaynağı (kullanıcı isteği: "öğrencilerin
// son 4 hafta devam grafiğini küçük badge'lerle gösterelim"). Öğrenci başına, içinde
// bulunulan hafta dahil son N takvim haftasının (pazartesi → pazar, okul saat dilimi)
// yoklama sayıları. People modülü bunu açık bir çağrıyla kullanır, Lessons'a kendisi join atmaz.
//
// Sayım kuralı "Yoklama" ekranıyla (AttendanceHistory) aynı: iptal edilen ve ertelenmiş (eski
// satır) dersler sayılmaz; henüz başlamamış ders yalnızca yoklaması erkenden girildiyse sayılır.
public static class RecentAttendanceWeeks
{
    public record Week(DateOnly WeekStart, int PresentCount, int AbsentCount, int ExcusedCount, int NotMarkedCount);

    public static async Task<Dictionary<Guid, List<Week>>> ForStudentsAsync(
        AbderaDbContext db, IClock clock, IReadOnlyCollection<Guid> studentIds, Guid? teacherScope, int weeks = 4)
    {
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);
        var currentWeekStart = StudentWeeklyLessonPolicy.StartOfWeek(today);
        var weekStarts = Enumerable.Range(0, weeks).Select(index => currentWeekStart.AddDays(-7 * (weeks - 1 - index))).ToList();
        var fromAt = LessonGenerator.ToUtcInstant(weekStarts[0], TimeOnly.MinValue, clock.SchoolTimeZone);
        var toAt = LessonGenerator.ToUtcInstant(currentWeekStart.AddDays(7), TimeOnly.MinValue, clock.SchoolTimeZone);

        var lessons = db.Lessons.AsNoTracking().Where(lesson =>
            studentIds.Contains(lesson.StudentId) &&
            lesson.StartAt >= fromAt && lesson.StartAt < toAt &&
            lesson.Status != LessonStatus.Cancelled && lesson.Status != LessonStatus.Rescheduled);
        // Öğretmen yalnızca kendi derslerinin yoklamasını görür (AttendanceHistory ile aynı kapsam).
        if (teacherScope is { } teacherId) lessons = lessons.Where(lesson => lesson.TeacherId == teacherId);

        var rows = await lessons
            .GroupJoin(db.LessonAttendances, lesson => lesson.Id, attendance => attendance.LessonId,
                (lesson, attendances) => new { lesson.StudentId, lesson.StartAt, Attendances = attendances })
            .SelectMany(item => item.Attendances.DefaultIfEmpty(),
                (item, attendance) => new { item.StudentId, item.StartAt, Status = attendance == null ? (Domain.AttendanceStatus?)null : attendance.Status })
            .Where(item => item.StartAt < now || item.Status != null)
            .ToListAsync();

        var byStudentWeek = rows.ToLookup(row => (row.StudentId,
            WeekStart: StudentWeeklyLessonPolicy.StartOfWeek(DateOnly.FromDateTime(clock.ToSchoolLocal(row.StartAt).Date))));

        var result = new Dictionary<Guid, List<Week>>();
        foreach (var studentId in studentIds)
        {
            result[studentId] = weekStarts.Select(weekStart =>
            {
                var inWeek = byStudentWeek[(studentId, weekStart)].ToList();
                return new Week(
                    weekStart,
                    inWeek.Count(row => row.Status == Domain.AttendanceStatus.Present),
                    inWeek.Count(row => row.Status == Domain.AttendanceStatus.Absent),
                    inWeek.Count(row => row.Status == Domain.AttendanceStatus.Excused),
                    inWeek.Count(row => row.Status == null));
            }).ToList();
        }
        return result;
    }
}
