using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Infrastructure;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Ops.Features;

// Tüm verinin Excel dökümü (docs/10-decisions.md AJ), yalnızca Admin. İstek hemen döner; dosyayı
// DataExportWorker arka planda üretir, ekran durumu yoklayıp hazır olunca indirir. Aynı anda tek
// döküm: sürmekte olan varken yeni istek onu döndürür. Liste son DataExport.KeepLatest dökümü gösterir.
//
// Döküm veri değiştirmez ama okulun bütün kişisel verisinin bir kopyası (KVKK): kimin ne zaman
// istediği ve indirdiği audit_log'a yazılır.
public static class DataExports
{
    public record DataExportResponse(
        Guid Id, DataExportStatus Status, string? FileName, int? SizeBytes, string? ErrorMessage,
        DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

    public static void MapDataExports(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/data-exports").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", ListAsync);
        group.MapPost("", RequestAsync);
        group.MapGet("/{id:guid}/file", DownloadAsync);
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db)
    {
        var exports = await Project(db.DataExports.AsNoTracking()
                .OrderByDescending(e => e.CreatedAt)
                .Take(DataExport.KeepLatest + 1))
            .ToListAsync();
        // Sürmekte olan döküm her zaman görünür; bitmişlerden en yeni KeepLatest tanesi.
        var running = exports.Where(e => e.Status is DataExportStatus.Pending or DataExportStatus.Running);
        var finished = exports.Except(running).Take(DataExport.KeepLatest);
        return Results.Ok(running.Concat(finished).ToList());
    }

    private static async Task<IResult> RequestAsync(ClaimsPrincipal principal, AbderaDbContext db, IClock clock, DataExportQueue queue)
    {
        var inProgress = await Project(db.DataExports.AsNoTracking()
                .Where(e => e.Status == DataExportStatus.Pending || e.Status == DataExportStatus.Running))
            .FirstOrDefaultAsync();
        if (inProgress is not null) return Results.Ok(inProgress);

        var actorId = AuthContext.GetUserId(principal);
        var export = DataExport.Request(actorId, clock.UtcNow);
        db.DataExports.Add(export);
        db.AuditLogs.Add(AuditLog.Record(actorId, "data_export.requested", nameof(DataExport), export.Id, clock.UtcNow));
        await db.SaveChangesAsync();
        queue.Enqueue(export.Id);

        return Results.Accepted($"/api/data-exports/{export.Id}", ToResponse(export));
    }

    private static async Task<IResult> DownloadAsync(Guid id, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var export = await db.DataExports.AsNoTracking()
            .Where(e => e.Id == id && e.Status == DataExportStatus.Completed)
            .Select(e => new { e.FileName, e.Content })
            .SingleOrDefaultAsync();
        if (export?.Content is null)
            return Results.Problem(title: "Döküm bulunamadı", detail: "Dosya silinmiş ya da henüz hazır değil.", statusCode: StatusCodes.Status404NotFound);

        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal), "data_export.downloaded", nameof(DataExport), id, clock.UtcNow,
            afterJson: JsonSerializer.Serialize(new { export.FileName })));
        await db.SaveChangesAsync();
        return Results.File(export.Content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", export.FileName);
    }

    private static IQueryable<DataExportResponse> Project(IQueryable<DataExport> query) =>
        query.Select(e => new DataExportResponse(e.Id, e.Status, e.FileName, e.SizeBytes, e.ErrorMessage, e.CreatedAt, e.CompletedAt));

    private static DataExportResponse ToResponse(DataExport e) =>
        new(e.Id, e.Status, e.FileName, e.SizeBytes, e.ErrorMessage, e.CreatedAt, e.CompletedAt);
}
