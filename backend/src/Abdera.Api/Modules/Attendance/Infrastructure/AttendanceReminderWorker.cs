using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Attendance.Infrastructure;

// "Ders sonrası bildirim gösterelim öğretmene": soru dersin bitişinden en geç bir aralık
// sonra düşer; zil 30 saniyede bir yoklandığı için öğretmen açık ekranda kartı hemen görür.
// LessonNoteReminderWorker (3 saat) burada fazla geç kalırdı - öğretmen dersten çıkıp
// bir sonrakine geçmeden sorulmalı.
//
// Açılışta HEMEN çalışmaz, ilk tur bir aralık sonra: integration testleri aynı
// WebApplicationFactory'yi paylaşıyor ve işi kendileri çağırıyor; açılışta çalışsaydı
// testle aynı satırı açmaya yarışırdı.
public class AttendanceReminderWorker(IServiceScopeFactory scopeFactory, ILogger<AttendanceReminderWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

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

            var result = await AttendanceReminderJob.RunAsync(db, clock, notifier, cancellationToken);
            if (result.AskedCount > 0 || result.ClearedCount > 0)
                logger.LogInformation(
                    "Yoklama sorusu: {Asked} ders için soruldu, {Cleared} soru kapandı.",
                    result.AskedCount, result.ClearedCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bir tur başarısız olursa uygulamayı düşürmez - bir sonraki tik'te tekrar dener.
            logger.LogError(ex, "Yoklama sorusu turu başarısız oldu.");
        }
    }
}
