using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Abdera.Api.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Progress.Features;

// Dönem karnesi (issue #15, docs/10-decisions.md Z). Öğretmen kendi kurs kayıtlarının karnesini
// yazar ve yayınlar; yönetici tüm okulun durumunu görür ve gerekirse aynı işlemleri yapar (ayrılan
// öğretmenin karnesi). Yayınlanan karne yalnızca veli portalında görünür - WhatsApp bildirimi yok
// (kullanıcı kararı). Para/takvim/rıza değiştirmediği için audit_log'a yazılmaz.
public static class TermReports
{
    public record SaveRequest(string? Comment);

    public record TermInfo(string Key, string Label, DateOnly Start, DateOnly End);

    public record PieceItem(string Title, string? Composer, RepertoireStatus? Status);
    public record SkillItem(string Label, int FirstScore, int LastScore);

    // Karnenin veri kısmı. Taslakta canlı hesaplanır, yayınlanınca bu haliyle SnapshotJson'a donar.
    public record Facts(
        string StudentName, string InstrumentName, string TeacherName,
        int LessonsPresent, int LessonsAbsent, int LessonsExcused,
        int PracticeMinutes, int PracticeDays,
        List<PieceItem> Pieces, List<SkillItem> Skills);

    public record ReportResponse(
        Guid? Id, Guid EnrollmentId, Guid StudentId, TermInfo Term, TermReportStatus? Status,
        string? Comment, DateTimeOffset? PublishedAt, DateTimeOffset? UpdatedAt, Facts Facts);

    public record ListRow(
        Guid EnrollmentId, Guid StudentId, string StudentName, string InstrumentName, Guid TeacherId, string TeacherName,
        TermReportStatus? Status, DateTimeOffset? UpdatedAt);

    public record ListResponse(TermInfo Term, TermInfo PreviousTerm, int PublishedCount, int DraftCount, List<ListRow> Rows);

    public record GuardianReport(Guid Id, TermInfo Term, string Comment, DateTimeOffset PublishedAt, Facts Facts);

    public static void MapTermReports(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/term-reports", ListAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        var single = app.MapGroup("/api/enrollments/{enrollmentId:guid}/term-reports/{term}")
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        single.MapGet("", GetAsync);
        single.MapPut("", SaveAsync);
        single.MapPost("/publish", PublishAsync);
        single.MapPost("/unpublish", UnpublishAsync);
        app.MapGet("/api/guardian/me/students/{studentId:guid}/term-reports", GuardianListAsync)
            .RequireAuthorization(AuthorizationPolicies.GuardianOnly)
            .RequireModule(ModuleKeys.ParentPortal);
    }

    private static async Task<IResult> ListAsync(string? term, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var reportTerm = ResolveTerm(term, clock);
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);

        // Dönemde dersi olan herkes: aktif kayıtlar + dönem içinde biten kayıtlar + karnesi zaten
        // açılmış kayıtlar (kayıt sonradan bitse bile yazılmış karne listeden düşmesin).
        var withReport = db.TermReports.Where(report => report.Term == reportTerm.Key).Select(report => report.EnrollmentId);
        var enrollments = db.Enrollments.Where(e =>
            e.StartedAt <= reportTerm.End
            && (e.Status == EnrollmentStatus.Active || (e.EndedAt != null && e.EndedAt >= reportTerm.Start) || withReport.Contains(e.Id)));
        if (teacherScope is { } teacherId) enrollments = enrollments.Where(e => e.TeacherId == teacherId);

        var rows = await enrollments
            .Join(db.Students, e => e.StudentId, s => s.Id, (e, s) => new { e, s })
            .Join(db.Instruments, x => x.e.InstrumentId, i => i.Id, (x, i) => new { x.e, x.s, i })
            .Join(db.Teachers, x => x.e.TeacherId, t => t.Id, (x, t) => new { x.e, x.s, x.i, t })
            .Where(x => x.s.Status == StudentStatus.Active || withReport.Contains(x.e.Id))
            .OrderBy(x => x.t.FirstName).ThenBy(x => x.s.FirstName).ThenBy(x => x.s.LastName)
            .Select(x => new
            {
                EnrollmentId = x.e.Id, StudentId = x.s.Id, StudentName = x.s.FirstName + " " + x.s.LastName,
                InstrumentName = x.i.Name, TeacherId = x.t.Id, TeacherName = x.t.FirstName + " " + x.t.LastName,
            })
            .ToListAsync();
        var enrollmentIds = rows.Select(row => row.EnrollmentId).ToList();
        var reports = await db.TermReports.AsNoTracking()
            .Where(report => report.Term == reportTerm.Key && enrollmentIds.Contains(report.EnrollmentId))
            .ToDictionaryAsync(report => report.EnrollmentId);

        var list = rows.Select(row =>
        {
            var report = reports.GetValueOrDefault(row.EnrollmentId);
            return new ListRow(row.EnrollmentId, row.StudentId, row.StudentName, row.InstrumentName, row.TeacherId, row.TeacherName,
                report?.Status, report?.UpdatedAt);
        }).ToList();
        return Results.Ok(new ListResponse(
            Info(reportTerm), Info(reportTerm.Previous()),
            list.Count(row => row.Status == TermReportStatus.Published),
            list.Count(row => row.Status == TermReportStatus.Draft),
            list));
    }

    private static async Task<IResult> GetAsync(Guid enrollmentId, string term, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var reportTerm = ParseTerm(term, clock);
        var enrollment = await LoadEnrollmentAsync(enrollmentId, principal, db);
        var report = await db.TermReports.AsNoTracking()
            .SingleOrDefaultAsync(item => item.EnrollmentId == enrollmentId && item.Term == reportTerm.Key);
        return Results.Ok(await ToResponseAsync(enrollment, reportTerm, report, db, clock));
    }

    private static async Task<IResult> SaveAsync(
        Guid enrollmentId, string term, SaveRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var reportTerm = ParseTerm(term, clock);
        var enrollment = await LoadEnrollmentAsync(enrollmentId, principal, db);
        var now = clock.UtcNow;
        var report = await db.TermReports.SingleOrDefaultAsync(item => item.EnrollmentId == enrollmentId && item.Term == reportTerm.Key);
        if (report is null)
        {
            report = TermReport.Create(enrollment.StudentId, enrollment.Id, enrollment.TeacherId, reportTerm, now);
            db.TermReports.Add(report);
        }
        report.SaveComment(request.Comment, now);
        await db.SaveChangesAsync();
        return Results.Ok(await ToResponseAsync(enrollment, reportTerm, report, db, clock));
    }

    private static async Task<IResult> PublishAsync(
        Guid enrollmentId, string term, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var reportTerm = ParseTerm(term, clock);
        var enrollment = await LoadEnrollmentAsync(enrollmentId, principal, db);
        var report = await db.TermReports.SingleOrDefaultAsync(item => item.EnrollmentId == enrollmentId && item.Term == reportTerm.Key)
            ?? throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["comment"] = ["Yayınlamadan önce öğretmen yorumunu yazıp kaydet."],
            });
        var facts = await BuildFactsAsync(enrollment, reportTerm, db, clock);
        report.Publish(JsonSerializer.Serialize(facts), AuthContext.GetUserId(principal), clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.Ok(await ToResponseAsync(enrollment, reportTerm, report, db, clock));
    }

    private static async Task<IResult> UnpublishAsync(
        Guid enrollmentId, string term, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var reportTerm = ParseTerm(term, clock);
        var enrollment = await LoadEnrollmentAsync(enrollmentId, principal, db);
        var report = await db.TermReports.SingleOrDefaultAsync(item => item.EnrollmentId == enrollmentId && item.Term == reportTerm.Key)
            ?? throw new NotFoundException("Karne bulunamadı.");
        report.Unpublish(clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.Ok(await ToResponseAsync(enrollment, reportTerm, report, db, clock));
    }

    private static async Task<IResult> GuardianListAsync(Guid studentId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var guardianId = AuthContext.GetUserId(principal);
        if (!await db.StudentGuardians.AnyAsync(link => link.StudentId == studentId && link.GuardianId == guardianId))
            throw new ForbiddenException("Bu öğrenci size bağlı değil.");

        // Veli yalnızca yayınlanmış karneyi ve onun dondurulmuş verisini görür; taslak hiç dönmez.
        var reports = await db.TermReports.AsNoTracking()
            .Where(report => report.StudentId == studentId && report.Status == TermReportStatus.Published)
            .ToListAsync();
        var result = reports
            .Select(report => (report, term: ReportTerm.TryParse(report.Term, out var parsed) ? parsed : (ReportTerm?)null))
            .Where(item => item.term is not null)
            .OrderByDescending(item => item.term!.Value.Start).ThenBy(item => item.report.CreatedAt)
            .Select(item => new GuardianReport(item.report.Id, Info(item.term!.Value), item.report.Comment!, item.report.PublishedAt!.Value,
                JsonSerializer.Deserialize<Facts>(item.report.SnapshotJson!)!))
            .ToList();
        return Results.Ok(result);
    }

    // Öğretmen yalnızca kendi kurs kaydının karnesine erişir; yönetici hepsine.
    private static async Task<Enrollment> LoadEnrollmentAsync(Guid enrollmentId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var enrollment = await db.Enrollments.AsNoTracking().SingleOrDefaultAsync(e => e.Id == enrollmentId)
            ?? throw new NotFoundException("Kurs kaydı bulunamadı.");
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is { } teacherId && teacherId != enrollment.TeacherId)
            throw new ForbiddenException("Bu kurs kaydı size atanmamış.");
        return enrollment;
    }

    private static ReportTerm ResolveTerm(string? term, IClock clock) =>
        term is null ? ReportTerm.Containing(Today(clock)) : ParseTerm(term, clock);

    private static ReportTerm ParseTerm(string term, IClock clock)
    {
        if (!ReportTerm.TryParse(term, out var parsed))
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["term"] = ["Geçersiz dönem."] });
        if (parsed.Start > Today(clock))
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["term"] = ["Henüz başlamamış bir dönemin karnesi yazılamaz."] });
        return parsed;
    }

    private static DateOnly Today(IClock clock) => DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);

    private static TermInfo Info(ReportTerm term) => new(term.Key, term.Label, term.Start, term.End);

    private static async Task<ReportResponse> ToResponseAsync(
        Enrollment enrollment, ReportTerm term, TermReport? report, AbderaDbContext db, IClock clock)
    {
        var facts = report is { Status: TermReportStatus.Published, SnapshotJson: { } snapshot }
            ? JsonSerializer.Deserialize<Facts>(snapshot)!
            : await BuildFactsAsync(enrollment, term, db, clock);
        return new ReportResponse(report?.Id, enrollment.Id, enrollment.StudentId, Info(term), report?.Status,
            report?.Comment, report?.PublishedAt, report?.UpdatedAt, facts);
    }

    // Dönemin verileri, yalnızca bu kurs kaydına ait derslerden (aynı öğrenci + öğretmen + enstrüman).
    // Pratik günlüğü öğrenci düzeyinde tutulduğu için öğrencinin tüm kayıtları için aynıdır.
    internal static async Task<Facts> BuildFactsAsync(Enrollment enrollment, ReportTerm term, AbderaDbContext db, IClock clock)
    {
        var fromAt = LessonGenerator.ToUtcInstant(term.Start, TimeOnly.MinValue, clock.SchoolTimeZone);
        var toAt = LessonGenerator.ToUtcInstant(term.End.AddDays(1), TimeOnly.MinValue, clock.SchoolTimeZone);

        var names = await db.Students.Where(s => s.Id == enrollment.StudentId)
            .Select(s => s.FirstName + " " + s.LastName).SingleAsync();
        var instrument = await db.Instruments.Where(i => i.Id == enrollment.InstrumentId).Select(i => i.Name).SingleAsync();
        var teacher = await db.Teachers.Where(t => t.Id == enrollment.TeacherId)
            .Select(t => t.FirstName + " " + t.LastName).SingleAsync();

        var lessons = db.Lessons.Where(l => l.StudentId == enrollment.StudentId && l.TeacherId == enrollment.TeacherId
            && l.InstrumentId == enrollment.InstrumentId && l.StartAt >= fromAt && l.StartAt < toAt);

        var attendance = await db.LessonAttendances
            .Join(lessons, a => a.LessonId, l => l.Id, (a, l) => a.Status)
            .GroupBy(status => status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count);

        var pieceRows = await db.LessonNotes
            .Where(n => n.PieceTitle != null)
            .Join(lessons, n => n.LessonId, l => l.Id, (n, l) => new { n.PieceTitle, n.PieceComposer, n.PieceStatus, n.CreatedAt })
            .ToListAsync();
        // Aynı eser birden çok notta geçer; en son notun bestecisi ve durumu geçerlidir.
        var pieces = pieceRows
            .GroupBy(row => row.PieceTitle!.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.OrderByDescending(row => row.CreatedAt).First())
            .OrderBy(row => row.CreatedAt)
            .Select(row => new PieceItem(row.PieceTitle!.Trim(), row.PieceComposer, row.PieceStatus))
            .ToList();

        var skillRows = await db.SkillAssessments
            .Where(a => a.StudentId == enrollment.StudentId && a.TeacherId == enrollment.TeacherId
                && a.AssessedAt >= fromAt && a.AssessedAt < toAt)
            .Join(db.SkillDefinitions, a => a.SkillDefinitionId, d => d.Id, (a, d) => new { d.Label, a.Score, a.AssessedAt })
            .ToListAsync();
        var skills = skillRows
            .GroupBy(row => row.Label)
            .Select(group =>
            {
                var ordered = group.OrderBy(row => row.AssessedAt).ToList();
                return new SkillItem(group.Key, ordered[0].Score, ordered[^1].Score);
            })
            .OrderBy(item => item.Label)
            .ToList();

        var practice = await db.PracticeJournalEntries
            .Where(p => p.StudentId == enrollment.StudentId && p.PracticeDate >= term.Start && p.PracticeDate <= term.End)
            .Select(p => new { p.PracticeDate, p.DurationMinutes })
            .ToListAsync();

        return new Facts(
            names, instrument, teacher,
            attendance.GetValueOrDefault(AttendanceStatus.Present),
            attendance.GetValueOrDefault(AttendanceStatus.Absent),
            attendance.GetValueOrDefault(AttendanceStatus.Excused),
            practice.Sum(p => p.DurationMinutes),
            practice.Select(p => p.PracticeDate).Distinct().Count(),
            pieces, skills);
    }
}
