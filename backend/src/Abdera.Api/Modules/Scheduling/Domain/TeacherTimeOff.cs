namespace Abdera.Api.Modules.Scheduling.Domain;

// docs/03-erd.md / docs/10-decisions.md A3 - öğretmen izni (hastalık, tatil). Ders üretimi
// bu aralığa denk gelen occurrence'ları atlar (docs/06-whatsapp.md ve generation mantığı).
public class TeacherTimeOff
{
    public Guid Id { get; private set; }
    public Guid TeacherId { get; private set; }
    public DateOnly StartsOn { get; private set; }
    public DateOnly EndsOn { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private TeacherTimeOff() { }

    public static TeacherTimeOff Create(Guid teacherId, DateOnly startsOn, DateOnly endsOn, string? reason, DateTimeOffset now)
    {
        if (endsOn < startsOn)
            throw new ArgumentException("Bitiş tarihi başlangıçtan önce olamaz.", nameof(endsOn));

        return new TeacherTimeOff
        {
            Id = Guid.NewGuid(),
            TeacherId = teacherId,
            StartsOn = startsOn,
            EndsOn = endsOn,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CreatedAt = now,
        };
    }

    public bool Covers(DateOnly date) => date >= StartsOn && date <= EndsOn;
}
