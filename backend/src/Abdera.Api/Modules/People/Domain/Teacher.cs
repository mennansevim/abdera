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
    public Guid Id { get; private set; }
    public Guid? UserId { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public TeacherStatus Status { get; private set; } = TeacherStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Teacher() { }

    public static Teacher Create(string firstName, string lastName, DateTimeOffset now, Guid? userId = null)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("Ad boş olamaz.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("Soyad boş olamaz.", nameof(lastName));

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
        if (string.IsNullOrWhiteSpace(firstName)) throw new ArgumentException("Ad boş olamaz.", nameof(firstName));
        if (string.IsNullOrWhiteSpace(lastName)) throw new ArgumentException("Soyad boş olamaz.", nameof(lastName));

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        UpdatedAt = now;
    }

    public void SetStatus(TeacherStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
    }
}
