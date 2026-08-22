using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Ops.Features;

// Dashboard ana ekranındaki sağlık kartı için - docs/15-product-phases.md Faz 4.
// Admin-only (docs/04-permissions.md - mali/altyapı verisi Teacher'a görünmez).
public static class SystemHealth
{
    public record SystemHealthResponse(
        SystemHealthLevel Level, string? Detail, DateTimeOffset LastCheckedAt,
        DateTimeOffset? LastSuccessfulBackupAt, string? LastBackupStatus);

    public static void MapSystemHealth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/system/health", GetAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> GetAsync(AbderaDbContext db)
    {
        var status = await SystemHealthStatus.GetCurrentAsync(db);
        var latestBackup = await db.BackupRuns.OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync();
        var lastSuccessful = await db.BackupRuns
            .Where(r => r.Status == BackupRunStatus.Succeeded)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync();

        return Results.Ok(new SystemHealthResponse(
            status.Level, status.Detail, status.LastCheckedAt,
            lastSuccessful?.CompletedAt, latestBackup?.Status.ToString()));
    }
}
