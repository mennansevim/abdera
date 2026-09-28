using System.Security.Claims;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Attendance.Features;

// "Yoklama" ekranının (frontend /dashboard/attendance) veri kaynağı: geçmişe dönük, öğretmen
// kırılımlı yoklama dökümü. MarkAttendance tek bir dersin yoklamasını yazar/okur; burada
// okulun tamamının geçmişi listelenir ve öğretmen başına özet çıkarılır.
// docs/04-permissions.md: Admin okul genelini görür, Teacher yalnızca kendi derslerini.
public static class AttendanceHistory
{
    // Yoklama girilmemiş geçmiş dersler de listelenir - yöneticinin "hangi ders işlendi ama
    // yoklaması hiç girilmedi" boşluğunu görebilmesi bu ekranın asıl faydalarından biri.
    // Bu yüzden filtre LessonAttendance.Status'ün kendisi değil, onu da kapsayan bir enum.
    public enum AttendanceFilter
    {
        Present,
        Absent,
        Excused,
        NotMarked,
    }

    public record HistoryItem(
        Guid LessonId, DateTimeOffset StartAt, DateTimeOffset EndAt, LessonStatus LessonStatus,
        Guid StudentId, string StudentName, Guid TeacherId, string TeacherName,
        Guid InstrumentId, string InstrumentName,
        AttendanceStatus? AttendanceStatus, DateTimeOffset? MarkedAt, string? Note);

    public record TeacherBreakdownItem(
        Guid TeacherId, string TeacherName,
        int LessonCount, int PresentCount, int AbsentCount, int ExcusedCount, int NotMarkedCount,
        DateTimeOffset? LastLessonAt);

    public record HistoryResponse(
        PagedResponse<HistoryItem> Lessons,
        IReadOnlyList<TeacherBreakdownItem> Teachers,
        int TotalLessonCount, int PresentCount, int AbsentCount, int ExcusedCount, int NotMarkedCount);

    public static void MapAttendanceHistory(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/attendance/history", ListAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    // Aralık verilmezse son 30 gün. Takvimdeki 3 aylık üst sınırın (Calendar.cs) buradaki
    // karşılığı yok: bu uç nokta sayfalanıyor, dönen satır sayısı aralıktan bağımsız olarak
    // pageSize ile sınırlı. Öğretmen kırılımı ise aralığın TAMAMI üzerinden hesaplanır -
    // sayfa 2'ye geçince özetin değişmemesi gerekir.
    private static readonly TimeSpan DefaultRange = TimeSpan.FromDays(30);

    private static async Task<IResult> ListAsync(
        DateTimeOffset? from, DateTimeOffset? to, Guid? teacherId, Guid? studentId, AttendanceFilter? status,
        int? page, int? pageSize, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var (normalizedPage, normalizedPageSize) = Pagination.Normalize(page, pageSize);
        var now = clock.UtcNow;
        var rangeEnd = to ?? now;
        var rangeStart = from ?? rangeEnd - DefaultRange;
        if (rangeEnd <= rangeStart)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["to"] = ["Bitiş tarihi başlangıçtan sonra olmalı."],
            });
        }

        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);

        // İşlenmiş sayılan dersler: iptal edilenler hiç yapılmadı, RESCHEDULED satırlar ise
        // yeni saatiyle zaten ayrı bir satır olarak listede - ikisi de dökümde yer almaz.
        var lessons = db.Lessons.Where(l =>
            l.StartAt >= rangeStart && l.StartAt < rangeEnd &&
            l.Status != LessonStatus.Cancelled && l.Status != LessonStatus.Rescheduled);

        if (teacherScope is { } scopedTeacherId)
        {
            lessons = lessons.Where(l => l.TeacherId == scopedTeacherId);
        }
        else if (teacherId is { } filterTeacherId)
        {
            lessons = lessons.Where(l => l.TeacherId == filterTeacherId);
        }
        if (studentId is { } filterStudentId)
        {
            lessons = lessons.Where(l => l.StudentId == filterStudentId);
        }

        // Yoklama kaydı olmayan ders de dönmeli, bu yüzden GroupJoin + SelectMany ile sol dış
        // birleştirme (LEFT JOIN) kuruluyor; öğrenci/öğretmen/enstrüman tarafları zorunlu.
        var rows = lessons
            .Join(db.Students, l => l.StudentId, s => s.Id, (l, s) => new { Lesson = l, Student = s })
            .Join(db.Teachers, x => x.Lesson.TeacherId, t => t.Id, (x, t) => new { x.Lesson, x.Student, Teacher = t })
            .Join(db.Instruments, x => x.Lesson.InstrumentId, i => i.Id, (x, i) => new { x.Lesson, x.Student, x.Teacher, Instrument = i })
            .GroupJoin(db.LessonAttendances, x => x.Lesson.Id, a => a.LessonId,
                (x, attendances) => new { x.Lesson, x.Student, x.Teacher, x.Instrument, Attendances = attendances })
            .SelectMany(x => x.Attendances.DefaultIfEmpty(),
                (x, attendance) => new { x.Lesson, x.Student, x.Teacher, x.Instrument, Attendance = attendance });

        // Ekran geriye dönük: henüz başlamamış dersler dökümde yer almaz - aksi halde
        // gelecek haftanın dersleri "yoklama girilmedi" olarak sayılır ve o sayaç anlamsızlaşır.
        // Öğretmen dersi erkenden işaretlediyse (yoklama kaydı varsa) satır yine görünür.
        rows = rows.Where(x => x.Lesson.StartAt < now || x.Attendance != null);

        rows = status switch
        {
            AttendanceFilter.Present => rows.Where(x => x.Attendance != null && x.Attendance.Status == Domain.AttendanceStatus.Present),
            AttendanceFilter.Absent => rows.Where(x => x.Attendance != null && x.Attendance.Status == Domain.AttendanceStatus.Absent),
            AttendanceFilter.Excused => rows.Where(x => x.Attendance != null && x.Attendance.Status == Domain.AttendanceStatus.Excused),
            AttendanceFilter.NotMarked => rows.Where(x => x.Attendance == null),
            _ => rows,
        };

        var totalCount = await rows.CountAsync();

        // Not: OrderBy, HistoryItem'a (record) projeksiyondan ÖNCE - EF Core bir record
        // constructor'ının alanına göre sıralamayı SQL'e çeviremiyor (bkz. CLAUDE.md ve
        // Calendar.cs'teki aynı not).
        var items = await rows
            .OrderByDescending(x => x.Lesson.StartAt)
            .ThenBy(x => x.Student.FirstName)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .Select(x => new HistoryItem(
                x.Lesson.Id, x.Lesson.StartAt, x.Lesson.EndAt, x.Lesson.Status,
                x.Student.Id, x.Student.FirstName + " " + x.Student.LastName,
                x.Teacher.Id, x.Teacher.FirstName + " " + x.Teacher.LastName,
                x.Instrument.Id, x.Instrument.Name,
                x.Attendance == null ? null : (Domain.AttendanceStatus?)x.Attendance.Status,
                x.Attendance == null ? null : (DateTimeOffset?)x.Attendance.MarkedAt,
                x.Attendance == null ? null : x.Attendance.Note))
            .ToListAsync();

        // Kırılım SAYFALANMAZ: aralığın tamamı üzerinden gruplanır, böylece sayfa
        // değiştirildiğinde öğretmen özeti sabit kalır. Gruplama sonucu anonim bir ara tipe
        // projekte edilip record'a bellekte çevriliyor (Calendar.cs ile aynı yaklaşım).
        var breakdownRows = await rows
            .GroupBy(x => new { x.Lesson.TeacherId, x.Teacher.FirstName, x.Teacher.LastName })
            .Select(group => new
            {
                group.Key.TeacherId,
                group.Key.FirstName,
                group.Key.LastName,
                LessonCount = group.Count(),
                PresentCount = group.Count(x => x.Attendance != null && x.Attendance.Status == Domain.AttendanceStatus.Present),
                AbsentCount = group.Count(x => x.Attendance != null && x.Attendance.Status == Domain.AttendanceStatus.Absent),
                ExcusedCount = group.Count(x => x.Attendance != null && x.Attendance.Status == Domain.AttendanceStatus.Excused),
                NotMarkedCount = group.Count(x => x.Attendance == null),
                LastLessonAt = group.Max(x => (DateTimeOffset?)x.Lesson.StartAt),
            })
            .ToListAsync();

        // Sıralama bellekte ve ordinal: adlandırılmış kültür (tr-TR) çağrısı yapılmıyor -
        // bu bir veri sıralaması, veliye gösterilecek bir metin değil (bkz. CLAUDE.md ICU notu).
        var breakdown = breakdownRows
            .Select(row => new TeacherBreakdownItem(
                row.TeacherId, row.FirstName + " " + row.LastName,
                row.LessonCount, row.PresentCount, row.AbsentCount, row.ExcusedCount, row.NotMarkedCount,
                row.LastLessonAt))
            .OrderByDescending(item => item.LessonCount)
            .ThenBy(item => item.TeacherName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Results.Ok(new HistoryResponse(
            new PagedResponse<HistoryItem>(items, totalCount, normalizedPage, normalizedPageSize),
            breakdown,
            totalCount,
            breakdown.Sum(item => item.PresentCount),
            breakdown.Sum(item => item.AbsentCount),
            breakdown.Sum(item => item.ExcusedCount),
            breakdown.Sum(item => item.NotMarkedCount)));
    }
}
