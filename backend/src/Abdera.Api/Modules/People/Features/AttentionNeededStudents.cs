using System.Security.Claims;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// "İlgi gerektirebilecek öğrenciler" - ayrılma riski erken uyarısı (issue #14). Sayılar
// burada toplanır, karar ve gerekçe AttentionSignal'dadır. Yalnızca aktif kurs kaydı olan
// aktif öğrenciler değerlendirilir; öğretmen yalnızca kendi öğrencilerini (ve kendi
// derslerindeki devamsızlık/katılım cevaplarını) görür, aidat sinyali ise yalnızca Admin'e
// hesaplanır (docs/04-permissions.md: aidat verisi Admin'e özel).
public static class AttentionNeededStudents
{
    public record Response(Guid StudentId, string StudentName, int RecentAbsenceCount, List<string> Reasons);

    public static void MapAttentionNeededStudents(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/students/attention-needed", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var teacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        var isAdmin = AuthContext.IsAdmin(principal);
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);

        var enrollments = db.Enrollments.Where(e => e.Status == EnrollmentStatus.Active);
        if (teacherId is { } scopedTeacherId)
            enrollments = enrollments.Where(e => e.TeacherId == scopedTeacherId);
        var students = await enrollments
            .Join(db.Students, e => e.StudentId, s => s.Id, (e, s) => s)
            .Where(s => s.Status == StudentStatus.Active)
            .Select(s => new { s.Id, Name = s.FirstName + " " + s.LastName })
            .Distinct()
            .ToListAsync();
        if (students.Count == 0) return Results.Ok(new List<Response>());
        var studentIds = students.Select(s => s.Id).ToList();

        var lessons = db.Lessons.Where(l => studentIds.Contains(l.StudentId));
        if (teacherId is { } lessonTeacherId)
            lessons = lessons.Where(l => l.TeacherId == lessonTeacherId);

        var absenceCutoff = now.AddDays(-AttentionSignal.AbsenceWindowDays);
        var absences = await db.LessonAttendances
            .Where(a => a.Status == AttendanceStatus.Absent)
            .Join(lessons, a => a.LessonId, l => l.Id, (a, l) => l)
            .Where(l => l.StartAt >= absenceCutoff)
            .GroupBy(l => l.StudentId)
            .Select(g => new { StudentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StudentId, x => x.Count);

        // Katılım cevabı yaklaşan ders için de verilir: pencere geçmiş 4 hafta + önümüzdeki hafta.
        var rsvpFrom = now.AddDays(-AttentionSignal.RsvpWindowDays);
        var rsvpTo = now.AddDays(7);
        var rsvps = await db.LessonRsvps
            .Where(r => r.Response == RsvpResponse.NotAttending || r.Response == RsvpResponse.AttendingLate)
            .Join(lessons, r => r.LessonId, l => l.Id, (r, l) => new { l.StudentId, l.StartAt, r.Response })
            .Where(x => x.StartAt >= rsvpFrom && x.StartAt < rsvpTo)
            .GroupBy(x => new { x.StudentId, x.Response })
            .Select(g => new { g.Key.StudentId, g.Key.Response, Count = g.Count() })
            .ToListAsync();

        var practiceRecentFrom = today.AddDays(-AttentionSignal.PracticeQuietDays);
        var practicePriorFrom = practiceRecentFrom.AddDays(-AttentionSignal.PracticePriorWindowDays);
        var practice = await db.PracticeJournalEntries
            .Where(p => studentIds.Contains(p.StudentId) && p.PracticeDate >= practicePriorFrom)
            .GroupBy(p => p.StudentId)
            .Select(g => new
            {
                StudentId = g.Key,
                Recent = g.Count(p => p.PracticeDate >= practiceRecentFrom),
                Prior = g.Count(p => p.PracticeDate < practiceRecentFrom),
            })
            .ToDictionaryAsync(x => x.StudentId);

        Dictionary<Guid, int>? overdue = null;
        if (isAdmin)
        {
            overdue = await db.Receivables
                .Where(r => r.Status == ReceivableStatus.Overdue)
                .Join(db.Enrollments, r => r.EnrollmentId, e => e.Id, (r, e) => e.StudentId)
                .Where(studentId => studentIds.Contains(studentId))
                .GroupBy(studentId => studentId)
                .Select(g => new { StudentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.StudentId, x => x.Count);
        }

        // Her (öğrenci, yetenek) için son iki puan: son puan pencere içindeyse ve bir öncekinden
        // düşükse sinyal. Veri küçük (öğrenci başına birkaç değerlendirme), eşleme bellekte yapılır.
        var skillCutoff = now.AddDays(-AttentionSignal.SkillDropWindowDays);
        var skillRows = await db.SkillAssessments
            .Where(a => studentIds.Contains(a.StudentId))
            .Join(db.SkillDefinitions, a => a.SkillDefinitionId, d => d.Id, (a, d) => new { a.StudentId, a.SkillDefinitionId, d.Label, a.Score, a.AssessedAt })
            .ToListAsync();
        var skillChanges = skillRows
            .GroupBy(x => new { x.StudentId, x.SkillDefinitionId, x.Label })
            .Select(g => new { g.Key, Latest = g.OrderByDescending(x => x.AssessedAt).Take(2).ToList() })
            .Where(x => x.Latest.Count == 2 && x.Latest[0].AssessedAt >= skillCutoff)
            .GroupBy(x => x.Key.StudentId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<AttentionSignal.SkillChange>)g
                    .OrderBy(x => x.Key.Label)
                    .Select(x => new AttentionSignal.SkillChange(x.Key.Label, x.Latest[1].Score, x.Latest[0].Score))
                    .ToList());

        var result = students
            .Select(student =>
            {
                var studentPractice = practice.GetValueOrDefault(student.Id);
                var signal = AttentionSignal.Evaluate(new AttentionSignal.Inputs(
                    RecentAbsenceCount: absences.GetValueOrDefault(student.Id),
                    RecentNotAttendingRsvpCount: rsvps.Where(r => r.StudentId == student.Id && r.Response == RsvpResponse.NotAttending).Sum(r => r.Count),
                    RecentLateRsvpCount: rsvps.Where(r => r.StudentId == student.Id && r.Response == RsvpResponse.AttendingLate).Sum(r => r.Count),
                    PracticeEntriesRecent: studentPractice?.Recent ?? 0,
                    PracticeEntriesPrior: studentPractice?.Prior ?? 0,
                    OverdueReceivableCount: overdue is null ? null : overdue.GetValueOrDefault(student.Id),
                    SkillChanges: skillChanges.GetValueOrDefault(student.Id)));
                return new { student, signal, absenceCount = absences.GetValueOrDefault(student.Id) };
            })
            .Where(x => x.signal.NeedsAttention)
            // Birden fazla sinyali olan öğrenci önce: tek bir devamsızlıktan daha acil.
            .OrderByDescending(x => x.signal.Reasons.Count)
            .ThenByDescending(x => x.absenceCount)
            .ThenBy(x => x.student.Name)
            .Select(x => new Response(x.student.Id, x.student.Name, x.absenceCount, x.signal.Reasons.ToList()))
            .ToList();
        return Results.Ok(result);
    }
}
