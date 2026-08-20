using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Infrastructure;

// docs/00-master-prompt.md: "A scheduler runs approximately once per minute and claims due
// jobs safely. Use a transaction and a locking strategy such as FOR UPDATE SKIP LOCKED."
// docs/06-whatsapp.md sequence diagram'ının worker tarafı - CLAUDE.md'nin Spring Scheduler
// karşılığı: BackgroundService + PeriodicTimer, ek kütüphane (Hangfire/Quartz) yok.
public class NotificationDispatcher(IServiceScopeFactory scopeFactory, ILogger<NotificationDispatcher> logger, IConfiguration config) : BackgroundService
{
    private const int BatchSize = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Varsayılan 60s (master prompt: "runs approximately once per minute"). Test'lerin
        // gerçek bir dakika beklemeden dispatcher'ı doğrulayabilmesi için yapılandırılabilir -
        // bkz. AbderaWebApplicationFactory'nin Notifications:DispatchIntervalSeconds override'ı.
        var intervalSeconds = config.GetValue("Notifications:DispatchIntervalSeconds", 60);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        do
        {
            await DispatchOnceAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var whatsAppClient = scope.ServiceProvider.GetRequiredService<IWhatsAppClient>();

            var now = clock.UtcNow;
            var quietStart = TimeOnly.Parse(config["Notifications:QuietHoursStart"] ?? "21:00");
            var quietEnd = TimeOnly.Parse(config["Notifications:QuietHoursEnd"] ?? "09:00");
            var maxAttempts = config.GetValue("Notifications:MaxAttempts", 5);

            // docs/06-whatsapp.md idempotency özeti: "Job işleme (worker eşzamanlılığı) ->
            // FOR UPDATE SKIP LOCKED - iki worker aynı job'ı asla iki kez işlemez." Kilit,
            // yalnızca bu transaction içinde tutulur; gönderim (yavaş I/O) transaction DIŞINDA yapılır.
            var pendingStatus = NotificationJobStatus.Pending.ToString();
            List<NotificationJob> claimedJobs;

            await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
            {
                var dueJobs = await db.NotificationJobs
                    .FromSqlInterpolated($@"
                        SELECT * FROM notification_jobs
                        WHERE status = {pendingStatus} AND scheduled_at <= {now}
                        ORDER BY scheduled_at
                        LIMIT {BatchSize}
                        FOR UPDATE SKIP LOCKED")
                    .ToListAsync(cancellationToken);

                claimedJobs = new List<NotificationJob>();
                foreach (var job in dueJobs)
                {
                    // A6: sessiz saat yalnızca cron kaynaklı tiplere uygulanır; burada (gönderimden
                    // hemen önce) kontrol edilir ki worker uzun süre çalışmasa da doğru davransın.
                    if (QuietHours.AppliesTo(job.Type))
                    {
                        var localTime = TimeOnly.FromDateTime(clock.ToSchoolLocal(now).DateTime);
                        if (QuietHours.IsWithinQuietHours(localTime, quietStart, quietEnd))
                        {
                            var nextWindow = QuietHours.ResolveSendTime(now, clock.SchoolTimeZone, quietStart, quietEnd);
                            job.Reschedule(nextWindow, now);
                            continue;
                        }
                    }

                    job.Claim(now);
                    claimedJobs.Add(job);
                }

                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            foreach (var job in claimedJobs)
            {
                await SendOneAsync(job, db, clock, whatsAppClient, maxAttempts, logger, cancellationToken);
            }

            if (claimedJobs.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bir tur başarısız olursa uygulamayı düşürmez - bir sonraki tik'te tekrar dener.
            logger.LogError(ex, "Bildirim dağıtım turu başarısız oldu.");
        }
    }

    private static async Task SendOneAsync(
        NotificationJob job, AbderaDbContext db, IClock clock, IWhatsAppClient whatsAppClient,
        int maxAttempts, ILogger logger, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        BuiltMessage? message;
        try
        {
            message = await NotificationMessageBuilder.BuildAsync(job, db, clock);
        }
        catch (NotImplementedNotificationTypeException)
        {
            // ARC-2: "ilgili kayıt bulunamıyor" gibi yanıltıcı bir hata yerine panelde
            // neden başarısız olduğu açıkça okunsun.
            job.MarkFailed("Bu bildirim tipi henüz uygulanmadı (Faz 7).", maxAttempts, now);
            return;
        }

        if (message is null)
        {
            job.MarkFailed("İlgili kayıt (ders/aidat) artık bulunamıyor veya bu tip için mesaj oluşturulamadı.", maxAttempts, now);
            return;
        }

        var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.PhoneNumber == job.RecipientPhoneNumber, cancellationToken);
        if (guardian is null)
        {
            job.MarkFailed("Alıcı telefon numarasına ait veli bulunamadı.", maxAttempts, now);
            return;
        }

        var template = await db.MessageTemplates.SingleOrDefaultAsync(t => t.Name == message.TemplateName && t.IsActive, cancellationToken);
        if (template is null)
        {
            job.MarkFailed($"'{message.TemplateName}' şablonu bulunamadı veya aktif değil.", maxAttempts, now);
            return;
        }

        var result = await whatsAppClient.SendTemplateAsync(job.RecipientPhoneNumber, template.Name, message.Parameters, cancellationToken);
        if (result.Success)
        {
            job.MarkSent(now);
            db.WhatsAppMessages.Add(WhatsAppMessage.CreateOutbound(
                job.Id, guardian.Id, template.Id, template.Render(message.Parameters), result.ProviderMessageId, now));
        }
        else
        {
            logger.LogWarning("Bildirim gönderilemedi (job {JobId}, deneme {Attempt}): {Error}", job.Id, job.AttemptCount, result.Error);
            job.MarkFailed(result.Error ?? "Bilinmeyen hata", maxAttempts, now);
        }
    }
}
