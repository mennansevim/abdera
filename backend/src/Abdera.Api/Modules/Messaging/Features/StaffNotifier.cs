using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Features;

// INotificationScheduler'ın (veliye WhatsApp) ekran içi karşılığı: diğer modüller personele
// bildirim düşürmek için Messaging'in iç tablolarına değil bu porta bağlanır.
//
// Kaydı ekler ama SaveChanges ÇAĞIRMAZ - çağıran handler zaten kendi değişikliğini (dersin
// yeni satırı, audit kaydı) tek transaction'da yazıyor; bildirimin de aynı transaction'a
// girmesi "ders taşındı ama bildirim düşmedi" ayrışmasını imkânsız kılar.
public interface IStaffNotifier
{
    /// <returns>Bildirim eklendiyse true; öğretmenin giriş hesabı yoksa (Teacher.UserId null) false.</returns>
    Task<bool> NotifyTeacherAsync(
        Guid teacherId,
        StaffNotificationType type,
        string title,
        string body,
        string referenceType,
        Guid referenceId);
}

public class StaffNotifier(AbderaDbContext db, IClock clock) : IStaffNotifier
{
    public async Task<bool> NotifyTeacherAsync(
        Guid teacherId,
        StaffNotificationType type,
        string title,
        string body,
        string referenceType,
        Guid referenceId)
    {
        // Öğretmenin giriş hesabı olmayabilir (Teacher.UserId nullable - yalnızca yönetici
        // tarafından yönetilen öğretmen). Bildirimi görecek bir ekran yoksa satır da açılmaz.
        var userId = await db.Teachers
            .Where(teacher => teacher.Id == teacherId)
            .Select(teacher => teacher.UserId)
            .SingleOrDefaultAsync();
        if (userId is not { } recipientId) return false;

        // Aynı olayın ikinci kez düşmesini veritabanı kısıtı da engelliyor; buradaki kontrol
        // istisnayı hiç doğurmadan sessizce geçmek için (örn. bir isteğin yeniden denenmesi).
        var alreadyExists = await db.StaffNotifications.AnyAsync(notification =>
            notification.UserId == recipientId &&
            notification.Type == type &&
            notification.ReferenceType == referenceType &&
            notification.ReferenceId == referenceId);
        if (alreadyExists) return false;

        db.StaffNotifications.Add(StaffNotification.Create(
            recipientId, type, title, body, referenceType, referenceId, clock.UtcNow));
        return true;
    }
}
