using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Infrastructure;
using Abdera.Api.Modules.Scheduling.Features;
using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Scheduling.Infrastructure;

// docs/10-decisions.md AM: açık serileri okul yılı sonuna kadar uzatır (SeriesExtension).
// Açılışta bir kez (deploy sonrası boşluk hemen dolsun), sonra günde bir. İdempotent: zaten
// üretilmiş günlere dokunmaz, iki kez çalışması zarar vermez. Scheduling:ExtendSeries=false
// ile kapatılır (entegrasyon testleri kendi ders sayılarını bozmasın diye kapatır).
public class SeriesExtensionWorker(
    IServiceScopeFactory scopeFactory, IConfiguration config, ILogger<SeriesExtensionWorker> logger,
    JobHeartbeatRecorder heartbeat) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Scheduling:ExtendSeries", true)) return;

        using var timer = new PeriodicTimer(BackgroundJobs.SeriesExtension.Interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var result = await SeriesExtension.ExtendAllAsync(
                services.GetRequiredService<AbderaDbContext>(),
                services.GetRequiredService<IClock>(),
                config,
                services.GetRequiredService<INotificationScheduler>(),
                cancellationToken);
            logger.LogInformation(
                "Ders serileri {Until} tarihine kadar uzatıldı: {Series} seri, {Created} yeni ders.",
                result.Until, result.SeriesVisited, result.LessonsCreated);
            await heartbeat.SucceededAsync(BackgroundJobs.SeriesExtension, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ders serisi uzatma başarısız oldu.");
            await heartbeat.FailedAsync(BackgroundJobs.SeriesExtension, ex, cancellationToken);
        }
    }
}
