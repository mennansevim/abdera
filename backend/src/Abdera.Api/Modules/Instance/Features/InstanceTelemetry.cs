using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Infrastructure;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Abdera.Api.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Instance.Features;

// Platformun okulu izlediği uçlar (docs/multi-tenant/ARCHITECTURE.md §5.4). Yalnızca SAYI döner:
// ad, telefon, e-posta, tutar yok - platform kişisel veriyi işlemez, yalnızca operatördür (KVKK).
public static class InstanceTelemetry
{
    public record HealthResponse(SystemHealthLevel Level, string? Detail, DateTimeOffset LastCheckedAt);
    public record CountsResponse(int ActiveStudents, int ActiveTeachers, int ActiveEnrollments);
    public record BackupResponse(DateTimeOffset? LastSucceededAt, BackupRunStatus? LastStatus, DateTimeOffset? LastStartedAt);
    public record NotificationsResponse(int Pending, int Failed);
    public record TelemetryResponse(
        string Version, DateTimeOffset GeneratedAt, DateTimeOffset? ProvisionedAt, HealthResponse Health, CountsResponse Counts,
        BackupResponse Backup, NotificationsResponse Notifications, long DatabaseSizeBytes, IReadOnlyList<string> EnabledModules);

    public static void MapInstanceTelemetry(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/instance/telemetry", GetAsync).AllowAnonymous().RequireRateLimiting("webhooks");
        app.MapPost("/api/instance/backup-now", BackupNowAsync).AllowAnonymous().RequireRateLimiting("webhooks");
    }

    private static async Task<IResult> GetAsync(
        HttpContext http, AbderaDbContext db, IConfiguration config, EnabledModules modules, IClock clock)
    {
        if (PlatformAccess.Check(http, config, PlatformAccess.TelemetryTokenKey) is { } denied) return denied;

        var health = await SystemHealthStatus.GetCurrentAsync(db);
        var lastBackup = await db.BackupRuns.AsNoTracking().OrderByDescending(run => run.StartedAt).FirstOrDefaultAsync();
        var lastSucceeded = await db.BackupRuns.AsNoTracking()
            .Where(run => run.Status == BackupRunStatus.Succeeded)
            .MaxAsync(run => (DateTimeOffset?)run.CompletedAt);
        var provisionedAt = await db.AuditLogs.AsNoTracking()
            .Where(log => log.Action == InstanceProvisioning.ProvisionedAction)
            .MinAsync(log => (DateTimeOffset?)log.CreatedAt);
        var databaseSize = await db.Database
            .SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"")
            .SingleAsync();

        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new TelemetryResponse(
            config["App:Version"] ?? "dev",
            clock.UtcNow,
            provisionedAt,
            new HealthResponse(health.Level, health.Detail, health.LastCheckedAt),
            new CountsResponse(
                await db.Students.CountAsync(student => student.Status == StudentStatus.Active),
                await db.Teachers.CountAsync(teacher => teacher.Status == TeacherStatus.Active),
                await db.Enrollments.CountAsync(enrollment => enrollment.Status == EnrollmentStatus.Active)),
            new BackupResponse(lastSucceeded, lastBackup?.Status, lastBackup?.StartedAt),
            new NotificationsResponse(
                await db.NotificationJobs.CountAsync(job => job.Status == NotificationJobStatus.Pending),
                await db.NotificationJobs.CountAsync(job => job.Status == NotificationJobStatus.Failed)),
            databaseSize,
            modules.Current.Order(StringComparer.Ordinal).ToList()));
    }

    // Platformun "şimdi yedek al" isteği (askıya alma ve silme öncesi son yedek, §16.3). Admin
    // ekranındaki tetikleyiciyle aynı servis; istek beklemeden 202 döner.
    private static IResult BackupNowAsync(
        HttpContext http, IConfiguration config, BackupService backupService, ILogger<BackupService> logger)
    {
        if (PlatformAccess.Check(http, config, PlatformAccess.TelemetryTokenKey) is { } denied) return denied;
        if (string.Equals(config["Backup:Provider"], BackupProviderModes.Disabled, StringComparison.OrdinalIgnoreCase))
            return Results.Problem(title: "Yedekleme devre dışı", detail: "Yedekleme sağlayıcısı yapılandırılmadı.",
                statusCode: StatusCodes.Status409Conflict);

        _ = Task.Run(async () =>
        {
            try { await backupService.TriggerManualRunAsync(); }
            catch (Exception ex) { logger.LogError(ex, "Platformun tetiklediği yedekleme beklenmedik şekilde başarısız oldu."); }
        });
        return Results.Accepted();
    }
}
