using System.Net.Mail;

namespace Abdera.Api.Modules.Auth.Domain;

// docs/03-erd.md - Auth > users
// No email channel exists in the MVP (docs/10-decisions.md B4): an admin resets a
// teacher's password to a temporary one and MustChangePassword forces a change on
// next login. There is no self-service "forgot password" flow.
public class User
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public bool MustChangePassword { get; private set; }
    public bool IsActive { get; private set; } = true;
    // Cookie oturumu doğrulanırken (Program.cs OnValidatePrincipal) bu değerle
    // karşılaştırılan opak bir değer - logout/şifre değişimi/hesap pasifleştirme burada
    // yenilenir, böylece o ana kadar geçerli olan eski cookie'ler (henüz süresi dolmamış
    // olsa bile) bir sonraki istekte reddedilir. Canlı QA turunda bulunan gerçek bug:
    // logout yalnızca istemci cookie'sini siliyordu, aynı eski cookie sunucuda hâlâ 12
    // saatlik sliding pencere boyunca geçerliydi.
    public Guid SecurityStamp { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private User() { }

    // UserConfiguration.cs'teki HasMaxLength(320) ile birebir.
    private const int MaxEmailLength = 320;

    public static User Create(string email, string passwordHash, UserRole role, DateTimeOffset now, bool mustChangePassword = false)
    {
        ValidateEmail(email);

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            Role = role,
            MustChangePassword = mustChangePassword,
            IsActive = true,
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void SetPassword(string passwordHash, DateTimeOffset now, bool mustChangePassword = false)
    {
        PasswordHash = passwordHash;
        MustChangePassword = mustChangePassword;
        // Şifre değişince (kendi isteğiyle veya admin resetiyle) eski oturumlar da düşer.
        SecurityStamp = Guid.NewGuid();
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        SecurityStamp = Guid.NewGuid();
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    // Logout - şifre değişmeden yalnızca mevcut oturumun sunucu tarafında geçersiz kılınması.
    public void InvalidateSessions(DateTimeOffset now)
    {
        SecurityStamp = Guid.NewGuid();
        UpdatedAt = now;
    }

    // Daha önce yalnızca boş kontrolü vardı - "abc" gibi "@" içermeyen bir değer de kabul
    // ediliyordu (canlı QA turunda bulundu). MailAddress ile ayrıştırma - ekstra bağımlılık
    // gerektirmeyen, .NET'in kendi hafif format kontrolü.
    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email boş olamaz.", nameof(email));
        if (email.Trim().Length > MaxEmailLength)
            throw new ArgumentException($"Email en fazla {MaxEmailLength} karakter olabilir.", nameof(email));
        try
        {
            _ = new MailAddress(email.Trim());
        }
        catch (FormatException)
        {
            throw new ArgumentException("Geçersiz e-posta adresi.", nameof(email));
        }
    }
}
