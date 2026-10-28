namespace Abdera.Api.Modules.Attendance.Domain;

// Yoklama kartına elle yazılan geçmiş ders (docs/10-decisions.md AD10). Okulun kâğıt listesi
// sisteme düzenli girilmediği için yönetici ilk giriş gününden sonraki geçmiş dersleri karta
// tarih olarak yazar; sonrasını sistemdeki yoklama doldurur. Bir "Geldi" satırı gibi sayılır
// ve dönem aidatını doğurabilir. Aynı güne sistemde ders varsa o esas alınır (çift sayılmaz).
public class ManualAttendanceEntry
{
    public Guid Id { get; private set; }
    public Guid EnrollmentId { get; private set; }
    public DateOnly Date { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ManualAttendanceEntry() { }

    public static ManualAttendanceEntry Create(Guid enrollmentId, DateOnly date, Guid? createdBy, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        EnrollmentId = enrollmentId,
        Date = date,
        CreatedBy = createdBy,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void ChangeDate(DateOnly date, DateTimeOffset now)
    {
        Date = date;
        UpdatedAt = now;
    }
}
