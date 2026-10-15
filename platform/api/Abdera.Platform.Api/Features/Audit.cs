using Abdera.Platform.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Api.Features;

// Platform denetim kaydı (§4.4). Salt okunur; kayıtlar silinmez.
public static class Audit
{
    public record AuditResponse(Guid Id, string? Actor, string Action, Guid? SchoolId, string? Slug, string? BeforeJson, string? AfterJson, DateTimeOffset CreatedAt);

    public static void MapAudit(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit", ListAsync).RequireAuthorization(Auth.OperatorPolicy);
    }

    private static async Task<IResult> ListAsync(PlatformDbContext db, Guid? schoolId, int? take)
    {
        var query = db.AuditLogs.AsQueryable();
        if (schoolId is { } id) query = query.Where(a => a.SchoolId == id);
        var logs = await query.OrderByDescending(a => a.CreatedAt).Take(Math.Clamp(take ?? 100, 1, 500)).ToListAsync();
        var users = await db.Users.ToDictionaryAsync(u => u.Id, u => u.Email);
        var slugs = await db.Schools.ToDictionaryAsync(s => s.Id, s => s.Slug);
        return Results.Ok(logs.Select(a => new AuditResponse(a.Id, a.ActorUserId is { } u ? users.GetValueOrDefault(u) : "sistem", a.Action,
            a.SchoolId, a.SchoolId is { } s ? slugs.GetValueOrDefault(s) : null, a.BeforeJson, a.AfterJson, a.CreatedAt)));
    }
}
