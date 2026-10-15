using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Infrastructure;
using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Progress.Infrastructure;

// Yorum hatırlatmasını periyodik olarak tazeler. İş idempotent ve "üç günde bir okunmamışa döndür"
// sınırını kendisi tuttuğu için sıklık yalnızca bir listenin ne kadar taze kalacağını belirler.
//
// OverdueReceivableSweeper'dan farkı: açılışta HEMEN çalışmaz, ilk tur bir aralık sonra.
// Integration testleri aynı WebApplicationFactory'yi paylaşıyor; açılışta çalışsaydı testin
// cron çağrısıyla aynı anda aynı satırı açmaya yarışırdı. Hatırlatma için birkaç saatlik
// gecikmenin bedeli yok.
public class LessonNoteReminderWorker(IServiceScopeFactory scopeFactory, ILogger<LessonNoteReminderWorker> logger, JobHeartbeatRecorder heartbeat) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var notifier = scope.ServiceProvider.GetRequiredService<IStaffNotifier>();

            var result = await LessonNoteReminderJob.RunAsync(db, clock, notifier, cancellationToken);
            if (result.RemindedCount > 0 || result.ClearedCount > 0)
                logger.LogInformation(
                    "Ders yorumu hatırlatması: {Reminded} öğretmene hatırlatıldı ({Pending} ders), {Cleared} hatırlatma kapandı.",
                    result.RemindedCount, result.PendingLessonCount, result.ClearedCount);

            await heartbeat.SucceededAsync(BackgroundJobs.LessonNoteReminder, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bir tur başarısız olursa uygulamayı düşürmez - bir sonraki tik'te tekrar dener.
            logger.LogError(ex, "Ders yorumu hatırlatma turu başarısız oldu.");
            await heartbeat.FailedAsync(BackgroundJobs.LessonNoteReminder, ex, cancellationToken);
        }
    }
}
