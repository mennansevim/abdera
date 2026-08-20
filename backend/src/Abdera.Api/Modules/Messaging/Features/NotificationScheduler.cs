using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Features;

// docs/06-whatsapp.md: "Scheduling/Billing doğrudan Meta API çağırmaz - INotificationScheduler
// benzeri bir port üzerinden NotificationJob oluşturur." Bu, diğer modüllerin Messaging'e
// bağımlı olduğu TEK nokta - port burada, kullanımı Scheduling/Billing'in kendi handler'larında.
public interface INotificationScheduler
{
    /// <returns>Job gerçekten oluşturulduysa true; rıza kapalıysa veya zaten varsa false.</returns>
    Task<bool> ScheduleAsync(
        NotificationJobType type, string referenceType, Guid referenceId, Guid guardianId, DateTimeOffset scheduledAt);

    /// <summary>docs/10-decisions.md A4: ders değişince/iptal olunca bekleyen job iptal edilir.</summary>
    Task CancelPendingAsync(string referenceType, Guid referenceId);
}

public class NotificationScheduler(AbderaDbContext db, IClock clock) : INotificationScheduler
{
    public async Task<bool> ScheduleAsync(
        NotificationJobType type, string referenceType, Guid referenceId, Guid guardianId, DateTimeOffset scheduledAt)
    {
        var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.Id == guardianId);
        // docs/06-whatsapp.md A8: rızası kapalı veliye asla job açılmaz.
        if (guardian is null || !guardian.NotificationConsent) return false;

        // A5 idempotency: aynı referans için zaten bekleyen/gönderilmiş bir job varsa tekrar açma.
        // (İptal edilmiş bir job'ın yerine yenisi açılabilmeli - A4'ün "yenisi kurulur" kuralı.)
        var alreadyExists = await db.NotificationJobs.AnyAsync(j =>
            j.Type == type && j.ReferenceType == referenceType && j.ReferenceId == referenceId &&
            j.Status != NotificationJobStatus.Cancelled);
        if (alreadyExists) return false;

        // Not: sessiz saat (A6) burada DEĞİL, NotificationDispatcher'da (worker) kontrol edilir -
        // docs/06-whatsapp.md: "worker tarafından gönderilmeden önce kontrol edilir." Burada
        // öteleseydik, worker uzun süre çalışmayıp scheduled_at'ten çok sonra devreye girdiğinde
        // job'ın YENİDEN sessiz saate düşüp düşmediğini asla tekrar kontrol edemezdik.
        db.NotificationJobs.Add(NotificationJob.Create(type, guardian.PhoneNumber, referenceType, referenceId, scheduledAt, clock.UtcNow));
        return true;
    }

    public async Task CancelPendingAsync(string referenceType, Guid referenceId)
    {
        var pendingJobs = await db.NotificationJobs
            .Where(j => j.ReferenceType == referenceType && j.ReferenceId == referenceId &&
                        (j.Status == NotificationJobStatus.Pending || j.Status == NotificationJobStatus.Processing))
            .ToListAsync();

        foreach (var job in pendingJobs)
        {
            job.Cancel(clock.UtcNow);
        }
    }
}
