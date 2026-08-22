using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Infrastructure;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Ops.Features;

// docs/07-api.md - Maliyet Takibi ile aynı yetki seviyesi: yalnızca Admin (mali/altyapı
// verisi, docs/04-permissions.md).
public static class BackupRuns
{
    public record BackupRunResponse(
        Guid Id, BackupRunStatus Status, bool TriggeredManually, DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt, long? SizeBytes, string? RemotePath, string? ErrorMessage);

    public static void MapBackupRuns(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/backup-runs").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", ListAsync);
        group.MapPost("/trigger", TriggerAsync);
    }

    private static async Task<IResult> ListAsync(int? page, int? pageSize, AbderaDbContext db)
    {
        var (normalizedPage, normalizedPageSize) = Pagination.Normalize(page, pageSize);

        var query = db.BackupRuns.OrderByDescending(r => r.StartedAt);
        var totalCount = await query.CountAsync();
        var runs = await query
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        return Results.Ok(new PagedResponse<BackupRunResponse>(runs.Select(ToResponse).ToList(), totalCount, normalizedPage, normalizedPageSize));
    }

    // Admin "şimdi yedek al" isteğini tetikler; gerçek iş BackupService içinde (aynı
    // BackgroundService, HTTP isteğini beklemeden arka planda tamamlanır) - bu yüzden
    // burada `await` edilmez, istek hemen 202 döner. BackupService.RunOnceAsync zaten
    // pg_dump/şifreleme/yükleme adımlarını kendi try/catch'iyle BackupRun.Failed'a
    // düşürüyor; burada yalnızca o noktaya hiç ulaşamayan (ör. scope oluşturma) beklenmedik
    // bir hatanın sessizce yutulmaması için ayrı bir log güvencesi var.
    private static IResult TriggerAsync(BackupService backupService, ILogger<BackupService> logger)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await backupService.TriggerManualRunAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Manuel yedekleme tetiklemesi beklenmedik şekilde başarısız oldu.");
            }
        });
        return Results.Accepted();
    }

    private static BackupRunResponse ToResponse(BackupRun run) => new(
        run.Id, run.Status, run.TriggeredManually, run.StartedAt, run.CompletedAt, run.SizeBytes, run.RemotePath, run.ErrorMessage);
}
