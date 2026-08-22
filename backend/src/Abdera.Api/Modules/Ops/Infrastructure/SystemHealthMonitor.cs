using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Abdera.Api.Modules.Ops.Infrastructure;

// Faz 4: "ana ekranda göster, sorun varsa kırmızı uyar + ilgililere mail at" - periyodik
// olarak veritabanı bağlantısını ve son başarılı yedeklemenin ne kadar eski olduğunu
// kontrol eder, SystemHealthStatus'u (tek satırlık, NotificationAutomationSettings ile
// aynı singleton desen) günceller. Aynı BackgroundService + PeriodicTimer deseni.
public class SystemHealthMonitor(
    IServiceScopeFactory scopeFactory,
    HealthCheckService healthCheckService,
    IConfiguration config,
    ILogger<SystemHealthMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = config.GetValue("Ops:HealthCheckIntervalMinutes", 10);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));
        do
        {
            await CheckOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var now = clock.UtcNow;

            var dbReport = await healthCheckService.CheckHealthAsync(cancellationToken);
            var dbHealthy = dbReport.Status == HealthStatus.Healthy;

            var latestBackup = await db.BackupRuns.OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(cancellationToken);
            var staleAfterHours = config.GetValue("Ops:BackupStaleAfterHours", 30);
            var unhealthyAfterHours = config.GetValue("Ops:BackupUnhealthyAfterHours", 48);
            var lastSuccessAge = latestBackup?.Status == BackupRunStatus.Succeeded
                ? now - (latestBackup.CompletedAt ?? latestBackup.StartedAt)
                : (TimeSpan?)null;

            var (level, detail) = Evaluate(dbHealthy, latestBackup, lastSuccessAge, staleAfterHours, unhealthyAfterHours);

            var status = await db.SystemHealthStatuses.SingleOrDefaultAsync(s => s.Id == SystemHealthStatus.SingletonId, cancellationToken);
            var isNew = status is null;
            status ??= SystemHealthStatus.CreateDefault(now);
            if (isNew) db.SystemHealthStatuses.Add(status);

            var previousLevel = status.Level;
            status.Update(level, detail, now);

            var cooldown = TimeSpan.FromMinutes(config.GetValue("Ops:AlertCooldownMinutes", 60));
            if (status.ShouldSendAlert(now, cooldown))
            {
                await SendAlertAsync(emailSender, config, level, detail, clock, now, cancellationToken);
                status.MarkAlertSent(now);
            }
            else if (level == SystemHealthLevel.Healthy && previousLevel != SystemHealthLevel.Healthy)
            {
                await SendRecoveryAlertAsync(emailSender, config, clock, now, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sağlık kontrolünün kendisi başarısız olursa uygulamayı düşürmez - bir sonraki
            // tik'te tekrar dener (OverdueReceivableSweeper ile aynı savunma deseni).
            logger.LogError(ex, "Sistem sağlık kontrolü başarısız oldu.");
        }
    }

    // public - Unit/OpsDomainTests.cs saf bir fonksiyon olarak doğrudan test ediyor
    // (PaymentMatcher.Match ile aynı desen: DB'ye dokunmayan karar mantığı ayrı test edilir).
    public static (SystemHealthLevel Level, string Detail) Evaluate(
        bool dbHealthy, BackupRun? latestBackup, TimeSpan? lastSuccessAge, int staleAfterHours, int unhealthyAfterHours)
    {
        if (!dbHealthy)
        {
            return (SystemHealthLevel.Unhealthy, "Veritabanı bağlantısı sağlıksız.");
        }
        if (latestBackup is null)
        {
            return (SystemHealthLevel.Degraded, "Henüz hiç yedekleme çalışmadı.");
        }
        if (latestBackup.Status == BackupRunStatus.Failed && lastSuccessAge is null)
        {
            return (SystemHealthLevel.Unhealthy, $"Son yedekleme başarısız oldu: {latestBackup.ErrorMessage}");
        }
        if (lastSuccessAge is null)
        {
            return (SystemHealthLevel.Degraded, "Son yedekleme hâlâ sürüyor veya sonucu belirsiz.");
        }
        if (lastSuccessAge.Value.TotalHours >= unhealthyAfterHours)
        {
            return (SystemHealthLevel.Unhealthy, $"Son başarılı yedekleme {lastSuccessAge.Value.TotalHours:0} saat önce - çok eski.");
        }
        if (lastSuccessAge.Value.TotalHours >= staleAfterHours)
        {
            return (SystemHealthLevel.Degraded, $"Son başarılı yedekleme {lastSuccessAge.Value.TotalHours:0} saat önce.");
        }
        return (SystemHealthLevel.Healthy, "Sistem sağlıklı.");
    }

    private static async Task SendAlertAsync(
        IEmailSender emailSender, IConfiguration config, SystemHealthLevel level, string detail, IClock clock, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var recipients = BackupService.ParseRecipients(config);
        if (recipients.Count == 0) return;

        await emailSender.SendAsync(recipients, $"Abdera - Sistem durumu: {level}",
            $"{detail}\n\nZaman: {clock.ToSchoolLocal(now):dd.MM.yyyy HH:mm}\n\nBu uyarı, aynı sorun devam ettiği sürece en fazla {config.GetValue("Ops:AlertCooldownMinutes", 60)} dakikada bir tekrar gönderilir.",
            cancellationToken);
    }

    private static async Task SendRecoveryAlertAsync(
        IEmailSender emailSender, IConfiguration config, IClock clock, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var recipients = BackupService.ParseRecipients(config);
        if (recipients.Count == 0) return;

        await emailSender.SendAsync(recipients, "Abdera - Sistem durumu düzeldi",
            $"Sistem tekrar sağlıklı durumda.\n\nZaman: {clock.ToSchoolLocal(now):dd.MM.yyyy HH:mm}",
            cancellationToken);
    }
}
