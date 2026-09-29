namespace Abdera.Api.Modules.Messaging.Domain;

public enum StaffNotificationType
{
    // Dersin günü/saati değişti - takvimden sürükle-bırak ya da ders detayından düzenleme.
    LessonMoved,
    // Bir öğretmen kendi öğrencisinin silinmesini talep etti; yönetici karara bağlamalı.
    StudentDeletionRequested,
    // Ders iptal edildi (telafi hakkı doğup doğmadığı metinde yazar).
    LessonCancelled,
    // Telafi hakkından yeni bir telafi dersi planlandı.
    MakeupScheduled,
    // Tamamlanan dersin ders notu (yorumu) hâlâ girilmemiş - tek bir olayı değil, süren bir
    // eksiği anlatan HATIRLATMA: öğretmen başına tek satır, iş bitene kadar tazelenir.
    LessonNoteMissing,
    // Bitmiş dersin yoklaması girilmemiş: "öğrenci geldi mi?" - ders başına bir satır, zilde
    // Geldi/Gelmedi düğmeleriyle cevaplanır. Okundu işaretlemekle kapanmaz; yalnızca yoklama
    // girilince (ya da ders iptal edilince) kapanır (AttendanceReminderJob).
    AttendanceMissing,
}

// docs/03-erd.md - Messaging > staff_notifications. WhatsApp tarafındaki NotificationJob
// veliye GİDEN mesajı temsil eder; bu tablo ise personelin (öğretmen/yönetici) uygulama
// içinde gördüğü bildirimdir - dışarı hiçbir şey gönderilmez, alıcı bir sonraki ekran
// yüklemesinde görür.
//
// Ayrı bir tablo olmasının nedeni: NotificationJob telefon numarası, gönderim durumu,
// deneme sayısı, sessiz saat gibi WhatsApp'a özgü alanlar taşır ve bir worker tarafından
// işlenir. Ekran içi bildirimin bunların hiçbirine ihtiyacı yok; aynı tabloya sıkıştırmak
// iki farklı durum makinesini tek satırda taşımak olurdu (docs/05-state-models.md).
public class StaffNotification
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public StaffNotificationType Type { get; private set; }
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public string ReferenceType { get; private set; } = null!;
    public Guid ReferenceId { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private StaffNotification() { }

    public static StaffNotification Create(
        Guid userId,
        StaffNotificationType type,
        string title,
        string body,
        string referenceType,
        Guid referenceId,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Bildirim başlığı boş olamaz.", nameof(title));
        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("Bildirim metni boş olamaz.", nameof(body));

        return new StaffNotification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Title = title.Trim(),
            Body = body.Trim(),
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    // Hatırlatma satırının (LessonNoteMissing gibi) metnini tazeler. Olay bildirimlerinden
    // farkı: aynı referans için ikinci satır açılmaz, var olan satır güncellenir. resurface
    // verildiğinde satır okunmamışa döner ve CreatedAt "hatırlatmanın tazelendiği an" olur -
    // liste/zil bu alana göre sıraladığı için hatırlatma yeniden öne çıkar. Çağıran günde en
    // fazla bir kez resurface eder (LessonNoteReminderJob), yoksa aynı gün içindeki her tur
    // okunmuş bir hatırlatmayı tekrar okunmamış yapardı.
    public void RefreshReminder(string title, string body, DateTimeOffset now, bool resurface)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Bildirim başlığı boş olamaz.", nameof(title));
        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("Bildirim metni boş olamaz.", nameof(body));

        Title = title.Trim();
        Body = body.Trim();
        if (resurface)
        {
            ReadAt = null;
            CreatedAt = now;
        }
        UpdatedAt = now;
    }

    // Tekrar çağrılırsa ilk okunma zamanı korunur - "okundu" geri alınabilir bir durum değil.
    // (Hatırlatmalar bunun istisnası: RefreshReminder bilerek okunmamışa döndürebilir.)
    public void MarkRead(DateTimeOffset now)
    {
        if (ReadAt is not null) return;
        ReadAt = now;
        UpdatedAt = now;
    }
}
