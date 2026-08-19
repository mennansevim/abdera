namespace Abdera.Api.Modules.Scheduling.Domain;

// docs/03-erd.md - Scheduling > teacher_availability. Öğretmenin haftalık düzenli uygunluk
// penceresi - LessonSeries oluşturulurken bu pencerenin içinde mi diye doğrulanır.
public class TeacherAvailability
{
    public Guid Id { get; private set; }
    public Guid TeacherId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public TimeOnly EndTime { get; private set; }

    private TeacherAvailability() { }

    public static TeacherAvailability Create(Guid teacherId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime)
    {
        if (endTime <= startTime)
            throw new ArgumentException("Bitiş saati başlangıçtan sonra olmalı.", nameof(endTime));

        return new TeacherAvailability
        {
            Id = Guid.NewGuid(),
            TeacherId = teacherId,
            DayOfWeek = dayOfWeek,
            StartTime = startTime,
            EndTime = endTime,
        };
    }

    public bool Covers(DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime) =>
        DayOfWeek == dayOfWeek && StartTime <= startTime && EndTime >= endTime;
}
