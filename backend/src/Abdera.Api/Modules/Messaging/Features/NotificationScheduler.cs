using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.People;
using Abdera.Api.Modules.Scheduling.Domain;
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

    /// <summary>A8: velinin bildirim onayı kapanınca ona giden bekleyen tüm job'lar iptal edilir.</summary>
    Task CancelPendingForRecipientAsync(string phoneNumber);

    /// <summary>
    /// Velinin bildirim onayı açılınca, birincil velisi olduğu öğrencilerin gelecekteki dersleri
    /// için ders hatırlatmalarını kurar (onay kapalıyken hiç kurulmamış ya da iptal edilmişlerdi).
    /// </summary>
    Task ScheduleUpcomingLessonRemindersAsync(Guid guardianId);
}

public class NotificationScheduler(AbderaDbContext db, IClock clock) : INotificationScheduler
{
    public async Task<bool> ScheduleAsync(
        NotificationJobType type, string referenceType, Guid referenceId, Guid guardianId, DateTimeOffset scheduledAt)
    {
        var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.Id == guardianId);
        // docs/06-whatsapp.md A8: rızası kapalı veliye asla job açılmaz.
        if (guardian is null || !guardian.NotificationConsent) return false;

        // Faz 3: ders hatırlatması otomasyonu admin panelden kapatılabilir - kapalıyken
        // yeni LessonReminder job'ı açılmaz (mevcut bekleyen job'lar ayrıca ayar değişince
        // Features/AutomationSettings.cs tarafından iptal edilir).
        if (type == NotificationJobType.LessonReminder)
        {
            var settings = await NotificationAutomationSettings.GetCurrentAsync(db);
            if (!settings.IsEnabled) return false;
        }

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

    public async Task CancelPendingForRecipientAsync(string phoneNumber)
    {
        var pendingJobs = await db.NotificationJobs
            .Where(j => j.RecipientPhoneNumber == phoneNumber &&
                        (j.Status == NotificationJobStatus.Pending || j.Status == NotificationJobStatus.Processing))
            .ToListAsync();

        foreach (var job in pendingJobs)
        {
            job.Cancel(clock.UtcNow);
        }
    }

    public async Task ScheduleUpcomingLessonRemindersAsync(Guid guardianId)
    {
        var guardian = await db.Guardians.SingleOrDefaultAsync(g => g.Id == guardianId);
        if (guardian is null || !guardian.NotificationConsent) return;

        var settings = await NotificationAutomationSettings.GetCurrentAsync(db);
        if (!settings.IsEnabled) return;

        var studentIds = await db.StudentGuardians
            .Where(sg => sg.GuardianId == guardianId)
            .Select(sg => sg.StudentId)
            .ToListAsync();

        var now = clock.UtcNow;
        foreach (var studentId in studentIds)
        {
            // Ders hatırlatması yalnızca birincil veliye gider (LessonSeriesFeatures ile aynı kural).
            if (await PrimaryGuardianResolver.ResolveAsync(db, studentId) != guardianId) continue;

            var lessons = await db.Lessons
                .Where(l => l.StudentId == studentId && l.StartAt > now &&
                            (l.Status == LessonStatus.Normal || l.Status == LessonStatus.Makeup))
                .Select(l => new { l.Id, l.StartAt })
                .ToListAsync();
            var lessonIds = lessons.Select(l => l.Id).ToList();
            var existingJobs = await db.NotificationJobs
                .Where(j => j.Type == NotificationJobType.LessonReminder && j.ReferenceType == "lesson" &&
                            lessonIds.Contains(j.ReferenceId))
                .ToDictionaryAsync(j => j.ReferenceId);

            foreach (var lesson in lessons)
            {
                var remindAt = lesson.StartAt.AddMinutes(-settings.LessonReminderMinutesBefore);
                // Hatırlatma anı geçmişse geç kalmış hatırlatma gönderme.
                if (remindAt <= now) continue;

                if (existingJobs.TryGetValue(lesson.Id, out var job))
                {
                    // Ders hâlâ geçerli olduğuna göre iptal onay/otomasyon kapanmasından geldi.
                    if (job.Status == NotificationJobStatus.Cancelled)
                        job.Reactivate(guardian.PhoneNumber, remindAt, now);
                    continue;
                }

                db.NotificationJobs.Add(NotificationJob.Create(
                    NotificationJobType.LessonReminder, guardian.PhoneNumber, "lesson", lesson.Id, remindAt, now));
            }
        }
    }
}
