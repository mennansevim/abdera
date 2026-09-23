using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Infrastructure;

// docs/05-state-models.md: "OVERDUE türetilmiş bir görünüm değil, saklanan bir durumdur -
// gecelik bir job due_date < today AND status IN (UNPAID, PARTIAL) olan kayıtları OVERDUE'ya
// çevirir." CLAUDE.md'nin Spring Scheduler karşılığı: BackgroundService + PeriodicTimer.
// Tarih granülerliğinde bir kontrol olduğu için saatlik çalışması yeterli - SKIP LOCKED
// gerekmiyor çünkü işlem idempotent bir toplu UPDATE (iki kez çalışsa da zarar vermez).
public class OverdueReceivableSweeper(IServiceScopeFactory scopeFactory, ILogger<OverdueReceivableSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        // Uygulama açılır açılmaz bir kere çalışır, sonra saatlik döngüye girer.
        do
        {
            await SweepOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            // Serverless ortamdaki cron ucu ile aynı kod yolu (BillingDailyJob).
            var count = await BillingDailyJob.MarkOverdueAsync(db, clock, cancellationToken);
            if (count > 0)
                logger.LogInformation("{Count} aidat OVERDUE olarak işaretlendi.", count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bir tarama başarısız olursa uygulamayı düşürmez - bir sonraki tik'te tekrar dener.
            logger.LogError(ex, "Vadesi geçmiş aidat taraması başarısız oldu.");
        }
    }
}
