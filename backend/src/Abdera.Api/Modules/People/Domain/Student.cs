namespace Abdera.Api.Modules.People.Domain;

// docs/03-erd.md - People > students. Mali/devamsızlık geçmişi korunduğu için gerçek
// silme yok - okuldan ayrılan öğrenci Inactive olur (CLAUDE.md).
public class Student
{
    // StudentConfiguration.cs'teki HasMaxLength(100) ile birebir - burada kontrol
    // edilmezse aşırı uzun bir ad DB'ye kadar gidip yakalanmayan bir DbUpdateException
    // (500) olarak patlıyordu; gerçek bir bug olarak canlı QA turunda bulundu.
    private const int MaxNameLength = 100;

    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public DateOnly BirthDate { get; private set; }
    public StudentStatus Status { get; private set; } = StudentStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Student() { }

    public static Student Create(string firstName, string lastName, DateOnly birthDate, DateTimeOffset now)
    {
        ValidateNames(firstName, lastName);
        ValidateBirthDate(birthDate, now);

        return new Student
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            BirthDate = birthDate,
            Status = StudentStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string firstName, string lastName, DateOnly birthDate, DateTimeOffset now)
    {
        ValidateNames(firstName, lastName);
        ValidateBirthDate(birthDate, now);

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        BirthDate = birthDate;
        UpdatedAt = now;
    }

    public void SetStatus(StudentStatus status, DateTimeOffset now)
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

    // Gelecekte veya makul olmayan ölçüde eski bir doğum tarihi (örn. 1500) daha önce hiç
    // reddedilmiyordu - canlı QA turunda 2099 ve 1500 doğum tarihleriyle 201 döndüğü
    // doğrulandı. Üst sınır okulun kapsamına göre cömert tutuldu (yetişkin öğrenci de olabilir).
    private static void ValidateBirthDate(DateOnly birthDate, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (birthDate > today) throw new ArgumentException("Doğum tarihi gelecekte olamaz.", nameof(birthDate));
        if (birthDate < today.AddYears(-120)) throw new ArgumentException("Doğum tarihi geçersiz görünüyor.", nameof(birthDate));
    }
}
