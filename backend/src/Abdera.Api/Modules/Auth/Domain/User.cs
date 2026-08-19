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
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private User() { }

    public static User Create(string email, string passwordHash, UserRole role, DateTimeOffset now, bool mustChangePassword = false)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email boş olamaz.", nameof(email));

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            Role = role,
            MustChangePassword = mustChangePassword,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void SetPassword(string passwordHash, DateTimeOffset now, bool mustChangePassword = false)
    {
        PasswordHash = passwordHash;
        MustChangePassword = mustChangePassword;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }
}
