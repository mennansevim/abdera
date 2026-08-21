namespace Abdera.Api.Modules.People.Domain;

// docs/10-decisions.md Karar F reversal: veli OTP ile giriş yapar (Modules/People/Features/GuardianAuth.cs).
// Kod kısa ömürlü (5 dakika) ve tek kullanımlıktır - bir telefon numarası için birden fazla
// satır birikebilir (her istek yenisini üretir), yalnızca en güncel tüketilmemiş/süresi
// geçmemiş satır geçerli sayılır.
public class GuardianLoginCode
{
    private const int MaxAttempts = 5;

    public Guid Id { get; private set; }
    public Guid GuardianId { get; private set; }
    public string CodeHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private GuardianLoginCode() { }

    public static GuardianLoginCode Create(Guid guardianId, string codeHash, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        GuardianId = guardianId,
        CodeHash = codeHash,
        ExpiresAt = now.AddMinutes(5),
        Attempts = 0,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public bool IsUsable(DateTimeOffset now) =>
        ConsumedAt is null && now < ExpiresAt && Attempts < MaxAttempts;

    public void RegisterFailedAttempt(DateTimeOffset now)
    {
        Attempts++;
        UpdatedAt = now;
    }

    public void MarkConsumed(DateTimeOffset now)
    {
        ConsumedAt = now;
        UpdatedAt = now;
    }
}
