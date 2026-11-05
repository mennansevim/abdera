using Abdera.Api.Shared;

namespace Abdera.Api.Modules.People.Domain;

public enum TeacherStatus
{
    Active,
    Inactive,
}

// docs/03-erd.md - People > teachers. UserId, öğretmenin giriş yapabildiği hesabı işaret
// eder (docs/10-decisions.md B4 - yönetici geçici şifre atar). Null olabilir: bir öğretmen
// henüz sisteme giriş yapmıyor olabilir (yalnızca yönetici tarafından yönetiliyor olabilir).
public class Teacher
{
    // TeacherConfiguration.cs'teki HasMaxLength(100) ile birebir - bkz. Student.cs'teki
    // aynı gerekçe (yakalanmayan DbUpdateException/500 canlı QA turunda bulundu).
    private const int MaxNameLength = 100;

    public Guid Id { get; private set; }
    public Guid? UserId { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public TeacherStatus Status { get; private set; } = TeacherStatus.Active;
    // Veli "gelemiyor"/"gecikecek" yanıtı verdiğinde öğretmene WhatsApp ile haber verilen numara
    // (docs/10-decisions.md AK). Boşsa öğretmen yalnızca ekran içi bildirimi alır.
    public string? PhoneNumber { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Teacher() { }

    public static Teacher Create(string firstName, string lastName, DateTimeOffset now, Guid? userId = null)
    {
        ValidateNames(firstName, lastName);

        return new Teacher
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Status = TeacherStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string firstName, string lastName, DateTimeOffset now)
    {
        ValidateNames(firstName, lastName);

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        UpdatedAt = now;
    }

    // Boş/null numara alanı temizler; dolu numara velidekiyle aynı biçime (E.164) normalize edilir.
    public void SetPhoneNumber(string? rawPhoneNumber, DateTimeOffset now)
    {
        PhoneNumber = string.IsNullOrWhiteSpace(rawPhoneNumber) ? null : PhoneNumberNormalizer.Normalize(rawPhoneNumber);
        UpdatedAt = now;
    }

    public void SetStatus(TeacherStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
    }

    private static void ValidateNames(string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("Ad boş olamaz.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("Soyad boş olamaz.", nameof(lastName));
        if (firstName.Trim().Length > MaxNameLength) throw new ArgumentException($"Ad en fazla {MaxNameLength} karakter olabilir.", nameof(firstName));
        if (lastName.Trim().Length > MaxNameLength) throw new ArgumentException($"Soyad en fazla {MaxNameLength} karakter olabilir.", nameof(lastName));
    }
}
