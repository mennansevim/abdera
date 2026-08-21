using Abdera.Api.Shared;

namespace Abdera.Api.Modules.People.Domain;

// docs/03-erd.md - People > guardians. conversation_window_expires_at (A7) ve
// notification_consent (A8) Messaging modülünün ihtiyaç duyduğu alanlar - veli için ayrı bir
// `users` satırı hâlâ yok (docs/10-decisions.md B4), bu bilgiler burada, People'da tutulur.
// Karar F reversal: veli artık telefon + WhatsApp OTP ile oturum açabiliyor (bkz.
// Modules/People/Features/GuardianAuth.cs) ama bu Guardian.Id üzerinden kurulur, `users`
// tablosuna hiç dokunmaz.
public class Guardian
{
    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public string PhoneNumber { get; private set; } = null!;
    public bool WhatsappEnabled { get; private set; } = true;
    public bool NotificationConsent { get; private set; } = true;
    public DateTimeOffset ConsentUpdatedAt { get; private set; }
    public DateTimeOffset? ConversationWindowExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Guardian() { }

    public static Guardian Create(string firstName, string lastName, string rawPhoneNumber, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("Ad boş olamaz.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("Soyad boş olamaz.", nameof(lastName));

        return new Guardian
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            PhoneNumber = PhoneNumberNormalizer.Normalize(rawPhoneNumber),
            WhatsappEnabled = true,
            NotificationConsent = true,
            ConsentUpdatedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string firstName, string lastName, string rawPhoneNumber, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("Ad boş olamaz.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("Soyad boş olamaz.", nameof(lastName));

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        PhoneNumber = PhoneNumberNormalizer.Normalize(rawPhoneNumber);
        UpdatedAt = now;
    }

    // docs/06-whatsapp.md A8 - "dur/iptal/stop" akışı bu metodu Phase 5'te çağıracak.
    public void SetNotificationConsent(bool consent, DateTimeOffset now)
    {
        NotificationConsent = consent;
        ConsentUpdatedAt = now;
        UpdatedAt = now;
    }

    // docs/06-whatsapp.md A7 - her gelen mesajda +24 saat tazelenir.
    public void RefreshConversationWindow(DateTimeOffset now)
    {
        ConversationWindowExpiresAt = now.AddHours(24);
        UpdatedAt = now;
    }
}
