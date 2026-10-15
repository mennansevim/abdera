namespace Abdera.Platform.Api.Domain;

// Platformdaki her yönetim işlemi (§3.2 platform_audit_log). JSON her zaman JsonSerializer ile
// kurulur (CLAUDE.md "Elle JSON string'i asla string interpolation ile kurma"). Silinmez.
public class PlatformAuditLog
{
    public Guid Id { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Action { get; private set; } = null!;
    public Guid? SchoolId { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PlatformAuditLog() { }

    public static PlatformAuditLog Record(Guid? actor, string action, Guid? schoolId, DateTimeOffset now, string? beforeJson = null, string? afterJson = null) => new()
    {
        Id = Guid.NewGuid(),
        ActorUserId = actor,
        Action = action,
        SchoolId = schoolId,
        BeforeJson = beforeJson,
        AfterJson = afterJson,
        CreatedAt = now,
    };
}

// Okuldan gelen alarm (Alerts__WebhookUrl, imzalı). Kişi verisi yok.
public class SchoolAlert
{
    public Guid Id { get; private set; }
    public Guid SchoolId { get; private set; }
    public string Kind { get; private set; } = null!;
    public string Level { get; private set; } = null!;
    public string Detail { get; private set; } = null!;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }

    private SchoolAlert() { }

    public static SchoolAlert Create(Guid schoolId, string kind, string level, string detail, DateTimeOffset occurredAt, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        SchoolId = schoolId,
        Kind = kind.Length > 40 ? kind[..40] : kind,
        Level = level.Length > 20 ? level[..20] : level,
        Detail = detail.Length > 1000 ? detail[..1000] : detail,
        OccurredAt = occurredAt,
        ReceivedAt = now,
    };

    public void Acknowledge(DateTimeOffset now) => AcknowledgedAt ??= now;
}

// Platform yöneticisi (okul kullanıcılarından tamamen ayrı). TOTP etkinleşmeden okul yönetilemez.
public class PlatformUser
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string? TotpSecretCiphertext { get; private set; }
    public bool TotpEnabled { get; private set; }
    // Son kabul edilen TOTP adımı: aynı kod iki kez kullanılamaz.
    public long LastTotpStep { get; private set; }
    public Guid SecurityStamp { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private PlatformUser() { }

    public static PlatformUser Create(string email, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Email = email.Trim().ToLowerInvariant(),
        PasswordHash = "placeholder",
        SecurityStamp = Guid.NewGuid(),
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void SetPasswordHash(string hash, DateTimeOffset now) { PasswordHash = hash; SecurityStamp = Guid.NewGuid(); UpdatedAt = now; }
    public void BeginTotpEnrollment(string ciphertext, DateTimeOffset now) { TotpSecretCiphertext = ciphertext; TotpEnabled = false; UpdatedAt = now; }
    public void EnableTotp(long step, DateTimeOffset now) { TotpEnabled = true; LastTotpStep = step; SecurityStamp = Guid.NewGuid(); UpdatedAt = now; }
    public void AcceptTotpStep(long step) => LastTotpStep = step;
}
