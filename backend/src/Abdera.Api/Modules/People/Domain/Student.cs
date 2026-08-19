namespace Abdera.Api.Modules.People.Domain;

// docs/03-erd.md - People > students. Mali/devamsızlık geçmişi korunduğu için gerçek
// silme yok - okuldan ayrılan öğrenci Inactive olur (CLAUDE.md).
public class Student
{
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
        if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("Ad boş olamaz.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("Soyad boş olamaz.", nameof(lastName));

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
        if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("Ad boş olamaz.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("Soyad boş olamaz.", nameof(lastName));

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
}
