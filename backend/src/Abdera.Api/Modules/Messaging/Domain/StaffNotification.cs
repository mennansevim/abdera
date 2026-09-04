namespace Abdera.Api.Modules.Messaging.Domain;

public enum StaffNotificationType
{
    // Dersin günü/saati değişti - takvimden sürükle-bırak ya da ders detayından düzenleme.
    LessonMoved,
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

    // Tekrar çağrılırsa ilk okunma zamanı korunur - "okundu" geri alınabilir bir durum değil.
    public void MarkRead(DateTimeOffset now)
    {
        if (ReadAt is not null) return;
        ReadAt = now;
        UpdatedAt = now;
    }
}
