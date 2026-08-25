namespace Abdera.Api.Modules.Scheduling.Domain;

public static class StudentWeeklyLessonPolicy
{
    public const int MaximumLessons = 4;

    public static DateOnly StartOfWeek(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysSinceMonday);
    }
}
