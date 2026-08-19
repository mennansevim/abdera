using Abdera.Api.Modules.Scheduling.Domain;

namespace Abdera.Tests.Unit;

// docs/09-testing.md: "Ders üretimi: rolling window (8–12 hafta), idempotency - iki kez
// çalıştırınca mükerrer satır olmamalı." Saf fonksiyon olduğu için veritabanı gerekmez.
public class LessonGeneratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly HashSet<DateOnly> NoDates = [];
    private static readonly List<(DateOnly, DateOnly)> NoTimeOff = [];

    private static LessonSeries CreateSeries(DayOfWeek day, DateOnly from, DateOnly? until = null) =>
        LessonSeries.Create(Guid.NewGuid(), day, new TimeOnly(18, 0), 45, from, until, Now);

    [Fact]
    public void Plan_generates_weekly_occurrences_within_window()
    {
        // 2026-08-19 bir Çarşamba - seri Salı günleri için, pencere 2026-08-19..2026-09-16 (4 hafta)
        var series = CreateSeries(DayOfWeek.Tuesday, new DateOnly(2026, 8, 19));
        var windowStart = new DateOnly(2026, 8, 19);
        var windowEnd = new DateOnly(2026, 9, 16);

        var plan = LessonGenerator.Plan(series, windowStart, windowEnd, NoDates, NoDates, NoTimeOff);

        Assert.Equal(4, plan.ToCreate.Count); // 25 Ağu, 1 Eyl, 8 Eyl, 15 Eyl
        Assert.All(plan.ToCreate, o => Assert.Equal(DayOfWeek.Tuesday, o.Date.DayOfWeek));
        Assert.Empty(plan.SkippedHolidays);
        Assert.Empty(plan.SkippedTeacherTimeOff);
        Assert.Empty(plan.AlreadyExists);
    }

    [Fact]
    public void Plan_is_idempotent_when_dates_already_exist()
    {
        var series = CreateSeries(DayOfWeek.Tuesday, new DateOnly(2026, 8, 19));
        var windowStart = new DateOnly(2026, 8, 19);
        var windowEnd = new DateOnly(2026, 9, 16);

        var firstRun = LessonGenerator.Plan(series, windowStart, windowEnd, NoDates, NoDates, NoTimeOff);
        var existingDates = firstRun.ToCreate.Select(o => o.Date).ToHashSet();

        var secondRun = LessonGenerator.Plan(series, windowStart, windowEnd, existingDates, NoDates, NoTimeOff);

        Assert.Empty(secondRun.ToCreate);
        Assert.Equal(firstRun.ToCreate.Count, secondRun.AlreadyExists.Count);
    }

    [Fact]
    public void Plan_skips_holiday_dates()
    {
        var series = CreateSeries(DayOfWeek.Tuesday, new DateOnly(2026, 8, 19));
        var holiday = new DateOnly(2026, 9, 1); // 1 Eylül Salı - tatil ilan edildi
        var holidays = new HashSet<DateOnly> { holiday };

        var plan = LessonGenerator.Plan(series, new DateOnly(2026, 8, 19), new DateOnly(2026, 9, 16), NoDates, holidays, NoTimeOff);

        Assert.Contains(holiday, plan.SkippedHolidays);
        Assert.DoesNotContain(plan.ToCreate, o => o.Date == holiday);
    }

    [Fact]
    public void Plan_skips_dates_covered_by_teacher_time_off()
    {
        var series = CreateSeries(DayOfWeek.Tuesday, new DateOnly(2026, 8, 19));
        var timeOff = new List<(DateOnly, DateOnly)> { (new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 4)) };

        var plan = LessonGenerator.Plan(series, new DateOnly(2026, 8, 19), new DateOnly(2026, 9, 16), NoDates, NoDates, timeOff);

        Assert.Contains(new DateOnly(2026, 9, 1), plan.SkippedTeacherTimeOff);
        Assert.DoesNotContain(plan.ToCreate, o => o.Date == new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void Plan_respects_effective_until_even_if_window_is_longer()
    {
        var series = CreateSeries(DayOfWeek.Tuesday, new DateOnly(2026, 8, 19), until: new DateOnly(2026, 8, 26));

        var plan = LessonGenerator.Plan(series, new DateOnly(2026, 8, 19), new DateOnly(2026, 9, 16), NoDates, NoDates, NoTimeOff);

        Assert.Single(plan.ToCreate); // yalnızca 25 Ağu - sonraki Salı (1 Eylül) EffectiveUntil'i (26 Ağu) aşıyor
        Assert.All(plan.ToCreate, o => Assert.True(o.Date <= new DateOnly(2026, 8, 26)));
    }

    [Fact]
    public void Plan_returns_nothing_when_effective_from_is_after_window()
    {
        var series = CreateSeries(DayOfWeek.Tuesday, new DateOnly(2027, 1, 1));

        var plan = LessonGenerator.Plan(series, new DateOnly(2026, 8, 19), new DateOnly(2026, 9, 16), NoDates, NoDates, NoTimeOff);

        Assert.Empty(plan.ToCreate);
    }

    [Fact]
    public void ToUtcInstant_converts_local_wall_clock_to_correct_utc_offset()
    {
        // Türkiye sabit UTC+3, DST yok (docs/10-decisions.md D5)
        var istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
        var instant = LessonGenerator.ToUtcInstant(new DateOnly(2026, 8, 25), new TimeOnly(18, 0), istanbul);

        Assert.Equal(new DateTimeOffset(2026, 8, 25, 15, 0, 0, TimeSpan.Zero), instant);
    }
}
