using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Ops.Domain;
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

    // Ders değişikliği (iptal / saat değişikliği / telafi) kullanıcı isteği: "bildirim gelsin,
    // mail gelsin". Alıcılar dersin öğretmeni + tüm aktif yöneticiler; işlemi yapan kişinin
    // kendisine düşmez (kendi yaptığı değişikliği ona bildirmek gürültü olur).
    // Ekran içi satırı ekler, e-postayı KUYRUĞA alır - gönderim FlushEmailsAsync ile,
    // SaveChanges başarılı olduktan SONRA yapılır ki kaydedilmemiş bir değişiklik için e-posta gitmesin.
    Task<int> NotifyLessonStaffAsync(
        Guid teacherId,
        Guid? actorUserId,
        StaffNotificationType type,
        string title,
        string body,
        string referenceType,
        Guid referenceId);

    // Tekrarlayan hatırlatma (olay değil, süren bir eksik): aynı referans için satır yoksa
    // açar, varsa metnini günceller - böylece zil "3 dersin notu eksik" yerine her ders için
    // ayrı bir satır göstermez. resurfaceIfSurfacedBefore verilir ve hatırlatma en son o andan
    // önce öne çıkmışsa (CreatedAt - RefreshReminder her öne çıkışta tazeler) okunmamışa döner.
    // UpdatedAt değil: tur her tik'te metni tazelediği için UpdatedAt hep yeni kalır ve
    // "birkaç günde bir dürt" eşiği hiç aşılmazdı.
    /// <returns>Hatırlatma gerçekten düştüyse (yeni açıldı veya okunmamışa döndü) true.</returns>
    Task<bool> RemindTeacherAsync(
        Guid teacherId,
        StaffNotificationType type,
        string title,
        string body,
        string referenceType,
        Guid referenceId,
        DateTimeOffset? resurfaceIfSurfacedBefore = null);

    // Hatırlatılacak bir şey kalmadığında (öğretmen eksikleri tamamladı) bekleyen hatırlatmayı
    // okundu sayar - aksi hâlde zil rozeti bitmiş bir iş için saymaya devam ederdi.
    /// <returns>Okunmamış bir hatırlatma kapatıldıysa true.</returns>
    Task<bool> ClearTeacherReminderAsync(
        Guid teacherId, StaffNotificationType type, string referenceType, Guid referenceId);

    // Referansa bağlı okunmamış hatırlatmayı KİM alıcı olursa olsun kapatır. Yoklama
    // hatırlatması dersin öğretmenine düşer, ama yoklamayı yönetici de girebilir ya da ders
    // sonradan başka öğretmene geçebilir - kapatan yolun alıcıyı bilmesi gerekmesin.
    /// <returns>Kapatılan satır sayısı.</returns>
    Task<int> ClearReminderForAllAsync(StaffNotificationType type, string referenceType, Guid referenceId);

    // Hâlâ açık (okunmamış) hatırlatmaların referansları - tur, artık geçerli olmayanları
    // (örn. iptal edilen dersin yoklama sorusu) kapatabilsin diye.
    Task<List<Guid>> OpenReminderReferenceIdsAsync(StaffNotificationType type, string referenceType);

    // Kuyruktaki e-postaları gönderir. Hata isteği düşürmez: değişiklik zaten kaydedildi ve
    // ekran içi bildirim yerinde; e-posta en iyi çabadır, başarısızlık loglanır.
    Task FlushEmailsAsync(CancellationToken cancellationToken = default);
}

public class StaffNotifier(
    AbderaDbContext db, IClock clock, IEmailSender emailSender, ILogger<StaffNotifier> logger) : IStaffNotifier
{
    private readonly List<(string To, string Subject, string Body)> _pendingEmails = [];

    public async Task<bool> NotifyTeacherAsync(
        Guid teacherId,
        StaffNotificationType type,
        string title,
        string body,
        string referenceType,
        Guid referenceId)
    {
        if (await ResolveTeacherUserIdAsync(teacherId) is not { } recipientId) return false;

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

    public async Task<int> NotifyLessonStaffAsync(
        Guid teacherId,
        Guid? actorUserId,
        StaffNotificationType type,
        string title,
        string body,
        string referenceType,
        Guid referenceId)
    {
        var teacherUserId = await db.Teachers
            .Where(teacher => teacher.Id == teacherId)
            .Select(teacher => teacher.UserId)
            .SingleOrDefaultAsync();

        var recipients = await db.Users
            .Where(user => user.IsActive && (user.Role == UserRole.Admin || user.Id == teacherUserId))
            .Where(user => actorUserId == null || user.Id != actorUserId)
            .Select(user => new { user.Id, user.Email })
            .ToListAsync();

        var added = 0;
        foreach (var recipient in recipients)
        {
            var alreadyExists = await db.StaffNotifications.AnyAsync(notification =>
                notification.UserId == recipient.Id &&
                notification.Type == type &&
                notification.ReferenceType == referenceType &&
                notification.ReferenceId == referenceId);
            if (alreadyExists) continue;

            db.StaffNotifications.Add(StaffNotification.Create(
                recipient.Id, type, title, body, referenceType, referenceId, clock.UtcNow));
            if (!string.IsNullOrWhiteSpace(recipient.Email))
                _pendingEmails.Add((recipient.Email, $"Abdera · {title}", $"{title}\n\n{body}\n\nAyrıntı için Abdera'da Ders Programı ekranına bakabilirsin."));
            added++;
        }

        return added;
    }

    public async Task<bool> RemindTeacherAsync(
        Guid teacherId,
        StaffNotificationType type,
        string title,
        string body,
        string referenceType,
        Guid referenceId,
        DateTimeOffset? resurfaceIfSurfacedBefore = null)
    {
        if (await ResolveTeacherUserIdAsync(teacherId) is not { } recipientId) return false;

        var existing = await db.StaffNotifications.SingleOrDefaultAsync(notification =>
            notification.UserId == recipientId &&
            notification.Type == type &&
            notification.ReferenceType == referenceType &&
            notification.ReferenceId == referenceId);

        if (existing is null)
        {
            db.StaffNotifications.Add(StaffNotification.Create(
                recipientId, type, title, body, referenceType, referenceId, clock.UtcNow));
            return true;
        }

        var resurface = resurfaceIfSurfacedBefore is { } threshold && existing.CreatedAt < threshold;
        existing.RefreshReminder(title, body, clock.UtcNow, resurface);
        return resurface;
    }

    public async Task<bool> ClearTeacherReminderAsync(
        Guid teacherId, StaffNotificationType type, string referenceType, Guid referenceId)
    {
        if (await ResolveTeacherUserIdAsync(teacherId) is not { } recipientId) return false;

        var existing = await db.StaffNotifications.SingleOrDefaultAsync(notification =>
            notification.UserId == recipientId &&
            notification.Type == type &&
            notification.ReferenceType == referenceType &&
            notification.ReferenceId == referenceId);
        if (existing is null || existing.ReadAt is not null) return false;

        existing.MarkRead(clock.UtcNow);
        return true;
    }

    public async Task<int> ClearReminderForAllAsync(StaffNotificationType type, string referenceType, Guid referenceId)
    {
        var open = await db.StaffNotifications
            .Where(notification =>
                notification.Type == type &&
                notification.ReferenceType == referenceType &&
                notification.ReferenceId == referenceId &&
                notification.ReadAt == null)
            .ToListAsync();
        foreach (var notification in open) notification.MarkRead(clock.UtcNow);
        return open.Count;
    }

    public Task<List<Guid>> OpenReminderReferenceIdsAsync(StaffNotificationType type, string referenceType) =>
        db.StaffNotifications
            .Where(notification =>
                notification.Type == type &&
                notification.ReferenceType == referenceType &&
                notification.ReadAt == null)
            .Select(notification => notification.ReferenceId)
            .Distinct()
            .ToListAsync();

    // Öğretmenin giriş hesabı olmayabilir (Teacher.UserId nullable - yalnızca yönetici
    // tarafından yönetilen öğretmen). Bildirimi görecek bir ekran yoksa satır da açılmaz.
    private async Task<Guid?> ResolveTeacherUserIdAsync(Guid teacherId) =>
        await db.Teachers
            .Where(teacher => teacher.Id == teacherId)
            .Select(teacher => teacher.UserId)
            .SingleOrDefaultAsync();

    public async Task FlushEmailsAsync(CancellationToken cancellationToken = default)
    {
        var batch = _pendingEmails.ToList();
        _pendingEmails.Clear();
        foreach (var (to, subject, body) in batch)
        {
            try
            {
                await emailSender.SendAsync([to], subject, body, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Personel e-postası gönderilemedi: {Subject}", subject);
            }
        }
    }
}
