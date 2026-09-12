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
    // GuardianConfiguration.cs'teki HasMaxLength(100) ile birebir - bkz. Student.cs'teki
    // aynı gerekçe (yakalanmayan DbUpdateException/500 canlı QA turunda bulundu).
    private const int MaxNameLength = 100;

    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public string PhoneNumber { get; private set; } = null!;
    public bool WhatsappEnabled { get; private set; } = true;
    public bool NotificationConsent { get; private set; } = true;
    public DateTimeOffset ConsentUpdatedAt { get; private set; }
    public DateTimeOffset? ConversationWindowExpiresAt { get; private set; }
    // Cookie oturumu doğrulanırken (Program.cs OnValidatePrincipal) karşılaştırılır -
    // bkz. User.cs'teki aynı gerekçe (logout eski cookie'yi sunucuda geçersiz kılar).
    public Guid SecurityStamp { get; private set; }
    // Karar F (ikinci) reversal: veli artık telefon + ŞİFRE ile giriş yapabiliyor
    // (docs/13-toplu-kurulum-ve-fix-list.md). Nullable - şifresi henüz üretilmemiş veli
    // yalnızca OTP ile girer. Hash IPasswordHasher<Guardian> ile üretilir (OTP kod hash'iyle
    // aynı altyapı), `users` tablosuna hâlâ dokunulmaz.
    public string? PasswordHash { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Guardian() { }

    public static Guardian Create(string firstName, string lastName, string rawPhoneNumber, DateTimeOffset now)
    {
        ValidateNames(firstName, lastName);

        return new Guardian
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            PhoneNumber = PhoneNumberNormalizer.Normalize(rawPhoneNumber),
            WhatsappEnabled = true,
            NotificationConsent = true,
            ConsentUpdatedAt = now,
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    // Logout - oturumu şifre değişmeden sunucu tarafında geçersiz kılar.
    public void InvalidateSessions(DateTimeOffset now)
    {
        SecurityStamp = Guid.NewGuid();
        UpdatedAt = now;
    }

    // Veli şifresini (yeniden) atar. Yeni şifreyle birlikte güvenlik damgası tazelenir ki
    // eski cookie oturumları sunucu tarafında düşsün (User.SetPassword ile aynı ruh).
    public void SetPassword(string passwordHash, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Şifre hash'i boş olamaz.", nameof(passwordHash));

        PasswordHash = passwordHash;
        SecurityStamp = Guid.NewGuid();
        UpdatedAt = now;
    }

    public void Update(string firstName, string lastName, string rawPhoneNumber, DateTimeOffset now)
    {
        ValidateNames(firstName, lastName);

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        PhoneNumber = PhoneNumberNormalizer.Normalize(rawPhoneNumber);
        UpdatedAt = now;
    }

    private static void ValidateNames(string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("Ad boş olamaz.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("Soyad boş olamaz.", nameof(lastName));
        if (firstName.Trim().Length > MaxNameLength) throw new ArgumentException($"Ad en fazla {MaxNameLength} karakter olabilir.", nameof(firstName));
        if (lastName.Trim().Length > MaxNameLength) throw new ArgumentException($"Soyad en fazla {MaxNameLength} karakter olabilir.", nameof(lastName));
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
