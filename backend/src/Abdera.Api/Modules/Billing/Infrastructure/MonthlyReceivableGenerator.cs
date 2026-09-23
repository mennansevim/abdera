using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Billing.Infrastructure;

// Kullanıcı kararı: her kayıt zaten en az 1 yıllık sabit haftalık ders taahhüdü, yani
// hangi aidatın açılacağı elle onaylanacak bir karar değil - MonthlyDueRun.BuildPlanAsync
// zaten deterministik bir "Ready" listesi üretiyor. Admin'in her ay "Aylık aidatları
// oluştur"a basmasını gerektiren akış bu yüzden kaldırıldı; aynı iş mantığı burada
// periyodik olarak (ve actorId=null ile, CLAUDE.md'nin sistem-kaynaklı olay kuralı)
// çağrılıyor. UNIQUE (enrollment_id, period) kısıtı ve BuildPlanAsync'in "existing"
// kontrolü sayesinde günde birden fazla çalışsa da zarar vermez (OverdueReceivableSweeper
// ile aynı gerekçe - LastRunDate gibi ayrı bir durum takibine gerek yok).
//
// Elle tetikleme tamamen kaldırılmadı: POST /api/receivables/monthly-run hâlâ AdminOnly
// olarak duruyor - bu servis uzun süre çalışmazsa (bkz. audit_log) veya geçmiş bir dönem
// telafi edilmek istenirse bir kaçış kapısı olarak kalır, yalnızca rutin arayüz yüzeyi
// (buton + önizleme modalı) kaldırıldı.
public class MonthlyReceivableGenerator(
    IServiceScopeFactory scopeFactory, ILogger<MonthlyReceivableGenerator> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        // Uygulama açılır açılmaz bir kere çalışır, sonra günlük döngüye girer - ayın
        // ilk günü konteyner o gün içinde herhangi bir saatte yeniden başlasa bile o
        // dönemin aidatları aynı gün açılır.
        do
        {
            await GenerateOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task GenerateOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            // Serverless ortamdaki cron ucu ile aynı kod yolu (BillingDailyJob).
            var result = await BillingDailyJob.OpenCurrentPeriodAsync(db, clock);
            var period = result.Period;

            if (result.CreatedCount > 0)
            {
                logger.LogInformation(
                    "{Period} dönemi için {Count} aidat otomatik oluşturuldu.", period, result.CreatedCount);
            }

            // Bu, geçerli bir ücret tarifesi hiç tanımlanmamış bir ders türü olduğu sürece
            // oluşur - okulun kendi kurulum eksikliği, sessizce atlanmaz ama akışı da
            // durdurmaz (CLAUDE.md: "sessizce 0 TL'lik aidat üretmez").
            if (result.Missing.Count > 0)
            {
                logger.LogWarning(
                    "{Period} dönemi: {Count} kurs kaydı için geçerli ücret tarifesi yok, aidat açılamadı: {Students}",
                    period, result.Missing.Count, string.Join(", ", result.Missing.Select(row => row.StudentName)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bir çalışma başarısız olursa uygulamayı düşürmez - bir sonraki tik'te tekrar
            // dener (aynı gerekçe: OverdueReceivableSweeper).
            logger.LogError(ex, "Aylık aidat otomatik üretimi başarısız oldu.");
        }
    }
}
