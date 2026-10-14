using System.Security.Claims;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Ops.Features;

// "Hata bildir" (docs/10-decisions.md W). Öğretmen ve yönetici gönderir; liste ve durum
// güncellemesi yalnızca yöneticiye açık. Görev canlıya çıkınca bildirim "Çözüldü" olur ve
// bildirimi yapan kendi çözülen bildirimlerini görüp "çalışıyor / hâlâ hatalı" diye teyit
// eder (W6). Ne istendiği anlaşılamayan bildirim "Beklemede" olur, bildirimi yapana soru
// gösterilir; cevabı açıklamaya eklenip bildirim yeniden incelemeye düşer (W7). Bildirim
// para/takvim/rıza değiştirmediği için audit_log'a yazılmaz.
public static class BugReports
{
    public record CreateRequest(BugReportKind Kind, string PagePath, string Description, string? AppVersion);
    public record CreateResponse(Guid Id);
    public record TriageRequest(BugReportStatus Status, int? GithubIssueNumber, string? TriageNote);
    public record ConfirmRequest(bool Works, string? Note);
    public record DetailsRequest(string Details);

    public record BugReportResponse(
        Guid Id, BugReportKind Kind, string PagePath, string Description, string? UserAgent, string? AppVersion,
        string? ReporterName, string? ReporterEmail, BugReportStatus Status, int? GithubIssueNumber,
        string? TriageNote, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt, string? FeedbackNote);

    public record StatusCounts(int New, int Triaged, int Rejected, int Resolved, int Verified, int Reopened, int NeedsInfo);
    public record ListResponse(PagedResponse<BugReportResponse> Page, StatusCounts Counts);

    public static void MapBugReports(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/bug-reports", CreateAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapGet("/api/bug-reports/awaiting-me", AwaitingMeAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/bug-reports/{id:guid}/details", ProvideDetailsAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/bug-reports/{id:guid}/confirmation", ConfirmAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

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

    // status birden çok verilebilir (?status=Resolved&status=Verified): ekrandaki sekmeler
    // birden fazla durumu bir arada gösterir.
    private static async Task<IResult> ListAsync(
        [Microsoft.AspNetCore.Mvc.FromQuery] BugReportStatus[]? status, int? page, int? pageSize, AbderaDbContext db)
    {
        var (pageNumber, size) = Pagination.Normalize(page, pageSize);
        var query = db.BugReports.AsNoTracking();
        if (status is { Length: > 0 }) query = query.Where(report => status.Contains(report.Status));

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

        var items = reports.Select(report => ToResponse(
            report,
            report.CreatedByUserId is { } id ? names.GetValueOrDefault(id) : null,
            report.CreatedByUserId is { } userId ? emails.GetValueOrDefault(userId) : null)).ToList();

        return Results.Ok(new ListResponse(
            new PagedResponse<BugReportResponse>(items, totalCount, pageNumber, size),
            new StatusCounts(
                countsByStatus.GetValueOrDefault(BugReportStatus.New),
                countsByStatus.GetValueOrDefault(BugReportStatus.Triaged),
                countsByStatus.GetValueOrDefault(BugReportStatus.Rejected),
                countsByStatus.GetValueOrDefault(BugReportStatus.Resolved),
                countsByStatus.GetValueOrDefault(BugReportStatus.Verified),
                countsByStatus.GetValueOrDefault(BugReportStatus.Reopened),
                countsByStatus.GetValueOrDefault(BugReportStatus.NeedsInfo))));
    }

    private static BugReportResponse ToResponse(BugReport report, string? reporterName, string? reporterEmail) => new(
        report.Id, report.Kind, report.PagePath, report.Description, report.UserAgent, report.AppVersion,
        reporterName, reporterEmail, report.Status, report.GithubIssueNumber, report.TriageNote, report.CreatedAt,
        report.ResolvedAt, report.FeedbackNote);

    // Oturumdaki kullanıcıdan bir şey bekleyen bildirimleri: çözüldü teyidi (W6) ya da
    // istenen detay (W7). Soru TriageNote alanında döner.
    private static async Task<IResult> AwaitingMeAsync(ClaimsPrincipal principal, AbderaDbContext db)
    {
        var userId = AuthContext.GetUserId(principal);
        var reports = await db.BugReports.AsNoTracking()
            .Where(report => report.CreatedByUserId == userId
                && (report.Status == BugReportStatus.Resolved || report.Status == BugReportStatus.NeedsInfo))
            .OrderByDescending(report => report.UpdatedAt)
            .ToListAsync();
        return Results.Ok(reports.Select(report => ToResponse(report, null, null)).ToList());
    }

    // Detayı bildirimi yapan verir; yönetici de onun adına yazabilir (telefonda öğrendiyse).
    private static async Task<IResult> ProvideDetailsAsync(
        Guid id, DetailsRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var report = await db.BugReports.SingleOrDefaultAsync(item => item.Id == id)
            ?? throw new NotFoundException("Bildirim bulunamadı.");
        if (report.CreatedByUserId != AuthContext.GetUserId(principal) && !AuthContext.IsAdmin(principal))
            throw new ForbiddenException("Yalnızca bildirimi yapan kişi detay ekleyebilir.");

        report.ProvideDetails(request.Details, clock.UtcNow, clock.SchoolTimeZone);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // Teyidi bildirimi yapan verir; yönetici de onun adına verebilir (yüz yüze sorduysa).
    private static async Task<IResult> ConfirmAsync(
        Guid id, ConfirmRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var report = await db.BugReports.SingleOrDefaultAsync(item => item.Id == id)
            ?? throw new NotFoundException("Bildirim bulunamadı.");
        if (report.CreatedByUserId != AuthContext.GetUserId(principal) && !AuthContext.IsAdmin(principal))
            throw new ForbiddenException("Yalnızca bildirimi yapan kişi teyit edebilir.");

        report.Confirm(request.Works, request.Note, clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> TriageAsync(Guid id, TriageRequest request, AbderaDbContext db, IClock clock)
    {
        var report = await db.BugReports.SingleOrDefaultAsync(item => item.Id == id)
            ?? throw new NotFoundException("Bildirim bulunamadı.");

        if (request.Status == BugReportStatus.Resolved) report.MarkResolved(clock.UtcNow);
        else report.Triage(request.Status, request.GithubIssueNumber, request.TriageNote, clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
