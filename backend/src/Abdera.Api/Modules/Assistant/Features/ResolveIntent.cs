using System.Security.Claims;
using Abdera.Api.Modules.Assistant.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Assistant.Features;

// POST /api/assistant/resolve - asistana yazılan cümlenin ne istediğini söyler; veri
// değiştirmez, audit yazmaz (docs/10-decisions.md AE). Kartların verisini frontend mevcut
// uç noktalardan çeker, yazma işlemleri de o ekranların kullandığı uç noktalardan geçer.
//
// Öğretmen yalnızca kendi öğrencileriyle eşleşir ve yalnızca kendi kurs kayıtlarını görür
// (GET /api/students ile aynı kapsam). Yöneticiye özel bir şey soran öğretmene hata değil
// Allowed=false döner: asistan "bu yalnız yöneticiye açık" diyebilsin.
public static class ResolveIntent
{
    public const int MaxTextLength = 300;
    private const int MaxStudents = 8;

    public record Request(string? Text, Guid? StudentId);

    public record CourseItem(Guid EnrollmentId, string InstrumentName, CourseKind CourseKind, string TeacherName);

    public record StudentItem(Guid StudentId, string FullName, StudentStatus Status, IReadOnlyList<CourseItem> Courses);

    public record LessonMove(
        DateOnly? SourceDate, TimeOnly? SourceTime,
        DateOnly? TargetDate, TimeOnly? TargetTime,
        RescheduleScope Scope);

    public record NavigationItem(string Key, string Label, string Path);

    public record Response(
        AssistantIntentKind Kind,
        bool Allowed,
        IReadOnlyList<StudentItem> Students,
        LessonMove? Move,
        NavigationItem? Navigation);

    public static void MapResolveIntent(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/assistant/resolve", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> HandleAsync(
        Request request,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var text = request.Text?.Trim() ?? "";
        if (text.Length == 0 || text.Length > MaxTextLength)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["text"] = [$"Mesaj boş olamaz ve en fazla {MaxTextLength} karakter olabilir."],
            });
        }

        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        var parsed = IntentParser.Parse(text);
        var tokens = AssistantText.Tokens(AssistantText.Normalize(text));

        var visibleStudents = db.Students.AsQueryable();
        if (teacherScope is { } scopedTeacherId)
        {
            var assignedStudentIds = db.Enrollments.Where(e => e.TeacherId == scopedTeacherId).Select(e => e.StudentId);
            visibleStudents = visibleStudents.Where(s => assignedStudentIds.Contains(s.Id));
        }

        var candidates = await visibleStudents
            .Select(s => new StudentNameMatcher.Candidate(s.Id, s.FirstName, s.LastName))
            .ToListAsync(cancellationToken);
        var match = StudentNameMatcher.Match(tokens, candidates);

        // Öğrenci satırındaki kısayoldan gelen istek: metinde ad yoksa bağlamdaki öğrenci
        // kullanılır. Görünmeyen bir öğrenci bağlam olarak verilirse yok sayılır.
        var studentIds = match.StudentIds.ToList();
        if (studentIds.Count == 0 && request.StudentId is { } contextId && candidates.Any(c => c.StudentId == contextId))
            studentIds.Add(contextId);

        var students = await LoadStudentsAsync(studentIds, teacherScope, db, cancellationToken);

        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var move = parsed.Kind == AssistantIntentKind.Reschedule
            ? new LessonMove(parsed.SourceDay?.Resolve(today), parsed.SourceTime, parsed.TargetDay?.Resolve(today), parsed.TargetTime, parsed.Scope)
            : null;
        var navigation = parsed.Navigation is { } target ? new NavigationItem(target.Key, target.Label, target.Path) : null;

        var isAdmin = teacherScope is null;
        var allowed = isAdmin || (parsed.Kind != AssistantIntentKind.Dues && parsed.Navigation?.AdminOnly != true);

        if (parsed.Kind == AssistantIntentKind.Unknown)
            LogUnmatched(loggerFactory, tokens, match.MatchedTokenIndexes);

        return Results.Ok(new Response(parsed.Kind, allowed, students, move, navigation));
    }

    private static async Task<IReadOnlyList<StudentItem>> LoadStudentsAsync(
        List<Guid> studentIds, Guid? teacherScope, AbderaDbContext db, CancellationToken cancellationToken)
    {
        if (studentIds.Count == 0) return [];

        var students = await db.Students
            .Where(s => studentIds.Contains(s.Id))
            .Select(s => new { s.Id, s.FirstName, s.LastName, s.Status })
            .ToListAsync(cancellationToken);

        var enrollments = db.Enrollments.Where(e => studentIds.Contains(e.StudentId) && e.Status == EnrollmentStatus.Active);
        if (teacherScope is { } scopedTeacherId) enrollments = enrollments.Where(e => e.TeacherId == scopedTeacherId);

        var courses = await enrollments
            .Join(db.Instruments, e => e.InstrumentId, i => i.Id, (e, i) => new { Enrollment = e, InstrumentName = i.Name })
            .Join(db.Teachers, x => x.Enrollment.TeacherId, t => t.Id, (x, t) => new
            {
                x.Enrollment.Id,
                x.Enrollment.StudentId,
                x.InstrumentName,
                x.Enrollment.CourseKind,
                TeacherFirstName = t.FirstName,
                TeacherLastName = t.LastName,
            })
            .OrderBy(x => x.InstrumentName).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        // Aktif öğrenci önce: ayrılmış bir öğrenci de sorulabilir (gelişim geçmişi), ama
        // aynı adı taşıyan aktif öğrencinin önüne geçmemeli.
        return students
            .OrderBy(s => s.Status == StudentStatus.Active ? 0 : 1)
            .ThenBy(s => s.FirstName).ThenBy(s => s.LastName)
            .Take(MaxStudents)
            .Select(s => new StudentItem(
                s.Id,
                $"{s.FirstName} {s.LastName}",
                s.Status,
                courses
                    .Where(c => c.StudentId == s.Id)
                    .Select(c => new CourseItem(c.Id, c.InstrumentName, c.CourseKind, $"{c.TeacherFirstName} {c.TeacherLastName}"))
                    .ToList()))
            .ToList();
    }

    // Kural yolunun kaçırdığı ifadeler LLM fazının ihtiyaç listesidir. Öğrenci adı loga
    // yazılmaz: eşleşen ad kelimeleri "<ogrenci>" ile maskelenir.
    private static void LogUnmatched(ILoggerFactory loggerFactory, string[] tokens, IReadOnlySet<int> nameIndexes)
    {
        var masked = string.Join(' ', tokens.Select((token, index) => nameIndexes.Contains(index) ? "<ogrenci>" : token));
        loggerFactory.CreateLogger(typeof(ResolveIntent)).LogInformation("Asistan ifadeyi eşleyemedi: {Text}", masked);
    }
}
