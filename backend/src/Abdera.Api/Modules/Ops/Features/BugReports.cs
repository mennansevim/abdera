using System.Security.Claims;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Ops.Features;

// "Hata bildir" (docs/10-decisions.md W). Öğretmen ve yönetici gönderir; liste ve durum
// güncellemesi yalnızca yöneticiye açık - öğretmen için gönder-unut, kendi bildirimini de
// listeleyemez. Bildirim para/takvim/rıza değiştirmediği için audit_log'a yazılmaz.
public static class BugReports
{
    public record CreateRequest(BugReportKind Kind, string PagePath, string Description, string? AppVersion);
    public record CreateResponse(Guid Id);
    public record TriageRequest(BugReportStatus Status, int? GithubIssueNumber, string? TriageNote);

    public record BugReportResponse(
        Guid Id, BugReportKind Kind, string PagePath, string Description, string? UserAgent, string? AppVersion,
        string? ReporterName, string? ReporterEmail, BugReportStatus Status, int? GithubIssueNumber,
        string? TriageNote, DateTimeOffset CreatedAt);

    public record StatusCounts(int New, int Triaged, int Rejected);
    public record ListResponse(PagedResponse<BugReportResponse> Page, StatusCounts Counts);

    public static void MapBugReports(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/bug-reports", CreateAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        var admin = app.MapGroup("/api/bug-reports").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        admin.MapGet("", ListAsync);
        admin.MapPatch("/{id:guid}", TriageAsync);
    }

    private static async Task<IResult> CreateAsync(
        CreateRequest request, HttpContext http, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var report = BugReport.Create(
            AuthContext.GetUserId(principal), request.Kind, request.PagePath, request.Description,
            http.Request.Headers.UserAgent.ToString(), request.AppVersion, clock.UtcNow);
        db.BugReports.Add(report);
        await db.SaveChangesAsync();
        return Results.Created($"/api/bug-reports/{report.Id}", new CreateResponse(report.Id));
    }

    private static async Task<IResult> ListAsync(
        BugReportStatus? status, int? page, int? pageSize, AbderaDbContext db)
    {
        var (pageNumber, size) = Pagination.Normalize(page, pageSize);
        var query = db.BugReports.AsNoTracking();
        if (status is not null) query = query.Where(report => report.Status == status);

        var totalCount = await query.CountAsync();
        var reports = await query
            .OrderByDescending(report => report.CreatedAt)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync();

        // Gönderenin adı People modülünden açık bir sorguyla okunur (CLAUDE.md: modüller arası
        // navigation yok). Yöneticinin öğretmen kaydı olmayabilir, o zaman yalnızca e-posta döner.
        var userIds = reports.Where(r => r.CreatedByUserId.HasValue).Select(r => r.CreatedByUserId!.Value).Distinct().ToList();
        var emails = await db.Users.AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Email);
        var names = await db.Teachers.AsNoTracking()
            .Where(teacher => teacher.UserId != null && userIds.Contains(teacher.UserId.Value))
            .ToDictionaryAsync(teacher => teacher.UserId!.Value, teacher => teacher.FirstName + " " + teacher.LastName);

        var countsByStatus = await db.BugReports.AsNoTracking()
            .GroupBy(report => report.Status)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count);

        var items = reports.Select(report => new BugReportResponse(
            report.Id, report.Kind, report.PagePath, report.Description, report.UserAgent, report.AppVersion,
            report.CreatedByUserId is { } id ? names.GetValueOrDefault(id) : null,
            report.CreatedByUserId is { } userId ? emails.GetValueOrDefault(userId) : null,
            report.Status, report.GithubIssueNumber, report.TriageNote, report.CreatedAt)).ToList();

        return Results.Ok(new ListResponse(
            new PagedResponse<BugReportResponse>(items, totalCount, pageNumber, size),
            new StatusCounts(
                countsByStatus.GetValueOrDefault(BugReportStatus.New),
                countsByStatus.GetValueOrDefault(BugReportStatus.Triaged),
                countsByStatus.GetValueOrDefault(BugReportStatus.Rejected))));
    }

    private static async Task<IResult> TriageAsync(Guid id, TriageRequest request, AbderaDbContext db, IClock clock)
    {
        var report = await db.BugReports.SingleOrDefaultAsync(item => item.Id == id)
            ?? throw new NotFoundException("Bildirim bulunamadı.");

        report.Triage(request.Status, request.GithubIssueNumber, request.TriageNote, clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
