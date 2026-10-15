using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Ops.Infrastructure;

// #31: arka plan işleri tur sonunda buraya bildirir. Kendi scope'unu (dolayısıyla kendi
// DbContext'ini) açar - işin context'i bir hatadan sonra kirli olabilir, ayrıca kayıt asla
// fırlatmaz: heartbeat yazılamadı diye işin kendisi düşmemeli.
public class JobHeartbeatRecorder(IServiceScopeFactory scopeFactory, ILogger<JobHeartbeatRecorder> logger)
{
    public Task SucceededAsync(BackgroundJob job, CancellationToken cancellationToken) =>
        WriteAsync(job, error: null, cancellationToken);

    public Task FailedAsync(BackgroundJob job, Exception exception, CancellationToken cancellationToken) =>
        WriteAsync(job, $"{exception.GetType().Name}: {exception.Message}", cancellationToken);

    private async Task WriteAsync(BackgroundJob job, string? error, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
            var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
            var name = job.Name;

            if (error is null)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO job_heartbeats (job_name, last_succeeded_at, created_at, updated_at)
                    VALUES ({name}, {now}, {now}, {now})
                    ON CONFLICT (job_name) DO UPDATE
                    SET last_succeeded_at = EXCLUDED.last_succeeded_at, updated_at = EXCLUDED.updated_at",
                    cancellationToken);
            }
            else
            {
                var trimmed = JobHeartbeat.TrimError(error);
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO job_heartbeats (job_name, last_failed_at, last_error, created_at, updated_at)
                    VALUES ({name}, {now}, {trimmed}, {now}, {now})
                    ON CONFLICT (job_name) DO UPDATE
                    SET last_failed_at = EXCLUDED.last_failed_at, last_error = EXCLUDED.last_error, updated_at = EXCLUDED.updated_at",
                    cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Job} için çalışma kaydı yazılamadı.", job.Name);
        }
    }
}
