using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Dashboard.Features;

// docs/13 Pillar F - öğrenci/öğretmen benchmark. Dashboard'ın salt-okunur cross-module
// istisnası kapsamında (bkz. Dashboard.cs başlığı): başka modüllerin tablolarını doğrudan
// açık LINQ ile okur. Her metrik için ayrı GroupBy sorgusu çalıştırıp bellekte birleştiririz
// (EF Core'da tek sorguda çok-tabloya correlated count kırılgan; Dashboard deseni). Kompozit
// skor set içindeki maksimuma göre normalize edilir - "öğrenci sayısı +, çalışma günü +,
// not/onaylı yorum +, katılım +".
public static class Benchmark
{
    public record TeacherRow(
        Guid TeacherId, string TeacherName, int ActiveStudents, int ActiveEnrollments,
        int Lessons, int Notes, int ApprovedComments, int Present, int Absent, int Excused,
        double AttendanceRate, double Score);

    public record StudentRow(
        Guid StudentId, string StudentName, int ActiveEnrollments, int Lessons,
        int Present, int Absent, double AttendanceRate, int NotesReceived, double Score);

    public static void MapBenchmark(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/benchmark").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("/teachers", TeachersAsync);
        group.MapGet("/students", StudentsAsync);
    }

    private static double Ratio(int part, int whole) => whole == 0 ? 0 : (double)part / whole;
    private static double Norm(int value, int max) => max == 0 ? 0 : (double)value / max;

    private static async Task<IResult> TeachersAsync(AbderaDbContext db)
    {
        var teachers = await db.Teachers
            .Where(t => t.Status == TeacherStatus.Active)
            .Select(t => new { t.Id, t.FirstName, t.LastName })
            .ToListAsync();

        // aktif kayıtlar (öğrenci sayısı + kayıt sayısı) - bellekte grupla (960 satır önemsiz)
        var activeEnrollments = await db.Enrollments
            .Where(e => e.Status == EnrollmentStatus.Active)
            .Select(e => new { e.TeacherId, e.StudentId })
            .ToListAsync();
        var studentsByTeacher = activeEnrollments.GroupBy(e => e.TeacherId)
            .ToDictionary(g => g.Key, g => new { Students = g.Select(x => x.StudentId).Distinct().Count(), Enrollments = g.Count() });

        // dersler (iptal/ertelenen hariç - çift sayımı önle)
        var lessonsByTeacher = (await db.Lessons
            .Where(l => l.Status != LessonStatus.Cancelled && l.Status != LessonStatus.Rescheduled)
            .GroupBy(l => l.TeacherId)
            .Select(g => new { TeacherId = g.Key, Count = g.Count() })
            .ToListAsync()).ToDictionary(x => x.TeacherId, x => x.Count);

        // notlar + onaylı veli yorumları
        var notesByTeacher = (await db.LessonNotes
            .GroupBy(n => n.TeacherId)
            .Select(g => new { TeacherId = g.Key, Notes = g.Count(), Approved = g.Count(n => n.ParentCommentApprovedAt != null) })
            .ToListAsync()).ToDictionary(x => x.TeacherId, x => x);

        // yoklama (işaretleyen öğretmen bazında)
        var attByTeacher = (await db.LessonAttendances
            .GroupBy(a => a.MarkedByTeacherId)
            .Select(g => new
            {
                TeacherId = g.Key,
                Present = g.Count(a => a.Status == AttendanceStatus.Present),
                Absent = g.Count(a => a.Status == AttendanceStatus.Absent),
                Excused = g.Count(a => a.Status == AttendanceStatus.Excused),
            })
            .ToListAsync()).ToDictionary(x => x.TeacherId, x => x);

        var rows = teachers.Select(t =>
        {
            studentsByTeacher.TryGetValue(t.Id, out var se);
            lessonsByTeacher.TryGetValue(t.Id, out var lessons);
            notesByTeacher.TryGetValue(t.Id, out var notes);
            attByTeacher.TryGetValue(t.Id, out var att);
            var present = att?.Present ?? 0; var absent = att?.Absent ?? 0; var excused = att?.Excused ?? 0;
            return new
            {
                t.Id, Name = $"{t.FirstName} {t.LastName}",
                Students = se?.Students ?? 0, Enrollments = se?.Enrollments ?? 0,
                Lessons = lessons, Notes = notes?.Notes ?? 0, Approved = notes?.Approved ?? 0,
                Present = present, Absent = absent, Excused = excused,
                AttendanceRate = Ratio(present, present + absent + excused),
            };
        }).ToList();

        int maxStudents = rows.DefaultIfEmpty().Max(x => x?.Students ?? 0);
        int maxLessons = rows.DefaultIfEmpty().Max(x => x?.Lessons ?? 0);
        int maxNotes = rows.DefaultIfEmpty().Max(x => x?.Notes ?? 0);
        int maxApproved = rows.DefaultIfEmpty().Max(x => x?.Approved ?? 0);

        var result = rows.Select(x =>
        {
            var score = 100.0 * (
                0.25 * Norm(x.Students, maxStudents) +
                0.25 * Norm(x.Lessons, maxLessons) +
                0.20 * Norm(x.Notes, maxNotes) +
                0.15 * Norm(x.Approved, maxApproved) +
                0.15 * x.AttendanceRate);
            return new TeacherRow(x.Id, x.Name, x.Students, x.Enrollments, x.Lessons, x.Notes, x.Approved,
                x.Present, x.Absent, x.Excused, Math.Round(x.AttendanceRate, 3), Math.Round(score, 1));
        }).OrderByDescending(r => r.Score).ThenBy(r => r.TeacherName).ToList();

        return Results.Ok(result);
    }

    private static async Task<IResult> StudentsAsync(AbderaDbContext db)
    {
        var students = await db.Students
            .Where(s => s.Status == StudentStatus.Active)
            .Select(s => new { s.Id, s.FirstName, s.LastName })
            .ToListAsync();

        var enrollmentsByStudent = (await db.Enrollments
            .Where(e => e.Status == EnrollmentStatus.Active)
            .GroupBy(e => e.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToListAsync()).ToDictionary(x => x.StudentId, x => x.Count);

        var lessonsByStudent = (await db.Lessons
            .Where(l => l.Status != LessonStatus.Cancelled && l.Status != LessonStatus.Rescheduled)
            .GroupBy(l => l.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToListAsync()).ToDictionary(x => x.StudentId, x => x.Count);

        // yoklama: attendance -> lesson (StudentId) join, öğrenci bazında grupla
        var attByStudent = (await (
            from a in db.LessonAttendances
            join l in db.Lessons on a.LessonId equals l.Id
            group a by l.StudentId into g
            select new
            {
                StudentId = g.Key,
                Present = g.Count(a => a.Status == AttendanceStatus.Present),
                Absent = g.Count(a => a.Status == AttendanceStatus.Absent),
                Excused = g.Count(a => a.Status == AttendanceStatus.Excused),
            }).ToListAsync()).ToDictionary(x => x.StudentId, x => x);

        // notlar: not -> lesson (StudentId) join
        var notesByStudent = (await (
            from n in db.LessonNotes
            join l in db.Lessons on n.LessonId equals l.Id
            group n by l.StudentId into g
            select new { StudentId = g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.StudentId, x => x.Count);

        var rows = students.Select(s =>
        {
            enrollmentsByStudent.TryGetValue(s.Id, out var enroll);
            lessonsByStudent.TryGetValue(s.Id, out var lessons);
            attByStudent.TryGetValue(s.Id, out var att);
            notesByStudent.TryGetValue(s.Id, out var notes);
            var present = att?.Present ?? 0; var absent = att?.Absent ?? 0; var excused = att?.Excused ?? 0;
            return new
            {
                s.Id, Name = $"{s.FirstName} {s.LastName}",
                Enrollments = enroll, Lessons = lessons, Present = present, Absent = absent,
                AttendanceRate = Ratio(present, present + absent + excused), Notes = notes,
            };
        }).ToList();

        int maxLessons = rows.DefaultIfEmpty().Max(x => x?.Lessons ?? 0);
        int maxNotes = rows.DefaultIfEmpty().Max(x => x?.Notes ?? 0);
        int maxEnroll = rows.DefaultIfEmpty().Max(x => x?.Enrollments ?? 0);

        var result = rows.Select(x =>
        {
            var score = 100.0 * (
                0.30 * Norm(x.Lessons, maxLessons) +
                0.30 * x.AttendanceRate +
                0.20 * Norm(x.Notes, maxNotes) +
                0.20 * Norm(x.Enrollments, maxEnroll));
            return new StudentRow(x.Id, x.Name, x.Enrollments, x.Lessons, x.Present, x.Absent,
                Math.Round(x.AttendanceRate, 3), x.Notes, Math.Round(score, 1));
        }).OrderByDescending(r => r.Score).ThenBy(r => r.StudentName).ToList();

        return Results.Ok(result);
    }
}
