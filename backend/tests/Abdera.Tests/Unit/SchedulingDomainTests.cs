using Abdera.Api.Modules.Scheduling.Domain;

namespace Abdera.Tests.Unit;

public class SchedulingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LessonSeries_Create_throws_when_duration_is_not_positive()
    {
        Assert.Throws<ArgumentException>(() => LessonSeries.Create(
            Guid.NewGuid(), DayOfWeek.Tuesday, new TimeOnly(18, 0), 0,
            new DateOnly(2026, 8, 19), null, Now));
    }

    [Fact]
    public void LessonSeries_Create_throws_when_effective_until_before_effective_from()
    {
        Assert.Throws<ArgumentException>(() => LessonSeries.Create(
            Guid.NewGuid(), DayOfWeek.Tuesday, new TimeOnly(18, 0), 45,
            new DateOnly(2026, 8, 19), new DateOnly(2026, 8, 1), Now));
    }

    [Fact]
    public void LessonSeries_EndTime_is_start_plus_duration()
    {
        var series = LessonSeries.Create(
            Guid.NewGuid(), DayOfWeek.Tuesday, new TimeOnly(18, 0), 45,
            new DateOnly(2026, 8, 19), null, Now);

        Assert.Equal(new TimeOnly(18, 45), series.EndTime);
    }

    [Fact]
    public void TeacherAvailability_Covers_returns_false_for_different_day()
    {
        var availability = TeacherAvailability.Create(
            Guid.NewGuid(), DayOfWeek.Tuesday, new TimeOnly(16, 0), new TimeOnly(20, 0));

        Assert.False(availability.Covers(DayOfWeek.Wednesday, new TimeOnly(18, 0), new TimeOnly(18, 45)));
    }

    [Fact]
    public void TeacherAvailability_Covers_returns_false_when_lesson_extends_past_window()
    {
        var availability = TeacherAvailability.Create(
            Guid.NewGuid(), DayOfWeek.Tuesday, new TimeOnly(16, 0), new TimeOnly(18, 30));

        // Ders 18:00-18:45, uygunluk 18:30'da bitiyor - kapsamıyor
        Assert.False(availability.Covers(DayOfWeek.Tuesday, new TimeOnly(18, 0), new TimeOnly(18, 45)));
    }

    [Fact]
    public void TeacherAvailability_Covers_returns_true_when_fully_within_window()
    {
        var availability = TeacherAvailability.Create(
            Guid.NewGuid(), DayOfWeek.Tuesday, new TimeOnly(16, 0), new TimeOnly(20, 0));

        Assert.True(availability.Covers(DayOfWeek.Tuesday, new TimeOnly(18, 0), new TimeOnly(18, 45)));
    }

    [Fact]
    public void TeacherTimeOff_Covers_is_inclusive_of_boundaries()
    {
        var timeOff = TeacherTimeOff.Create(Guid.NewGuid(), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), "Tatil", Now);

        Assert.True(timeOff.Covers(new DateOnly(2026, 9, 1)));
        Assert.True(timeOff.Covers(new DateOnly(2026, 9, 5)));
        Assert.False(timeOff.Covers(new DateOnly(2026, 8, 31)));
        Assert.False(timeOff.Covers(new DateOnly(2026, 9, 6)));
    }

    [Fact]
    public void Lesson_CreateFromSeries_throws_when_end_before_start()
    {
        var start = new DateTimeOffset(2026, 8, 25, 15, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => Lesson.CreateFromSeries(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, start, Now));
    }
}
