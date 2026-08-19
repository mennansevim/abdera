namespace Abdera.Api.Modules.Scheduling.Domain;

public enum SchoolCalendarDayType
{
    Holiday,
    Event,
}

// docs/03-erd.md - Scheduling > school_calendar_days. Resmi tatiller (ders üretimi atlar)
// ve okul etkinlikleri (resital vb. - docs/10-decisions.md C5, ayrı entity açılmadı).
public class SchoolCalendarDay
{
    public Guid Id { get; private set; }
    public DateOnly Date { get; private set; }
    public SchoolCalendarDayType Type { get; private set; }
    public string Label { get; private set; } = null!;

    private SchoolCalendarDay() { }

    public static SchoolCalendarDay Create(DateOnly date, SchoolCalendarDayType type, string label)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Etiket boş olamaz.", nameof(label));

        return new SchoolCalendarDay
        {
            Id = Guid.NewGuid(),
            Date = date,
            Type = type,
            Label = label.Trim(),
        };
    }
}
