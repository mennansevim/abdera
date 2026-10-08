using Abdera.Api.Modules.Scheduling.Domain;

namespace Abdera.Tests.Unit;

public class OpenSlotFinderTests
{
    private static OpenSlotFinder.Window Window(DayOfWeek day, int startHour, int endHour) =>
        new(day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    private static List<string> Format(IEnumerable<OpenSlotFinder.Slot> slots) =>
        slots.Select(s => $"{s.DayOfWeek} {s.StartTime:HH\\:mm}").ToList();

    [Fact]
    public void Free_starts_fill_the_window_in_quarter_hour_steps_and_the_lesson_must_end_inside_it()
    {
        var slots = OpenSlotFinder.WeeklyFreeStarts([Window(DayOfWeek.Tuesday, 15, 17)], [], 45);

        Assert.Equal(
            ["Tuesday 15:00", "Tuesday 15:15", "Tuesday 15:30", "Tuesday 15:45", "Tuesday 16:00", "Tuesday 16:15"],
            Format(slots));
    }

    [Fact]
    public void A_series_blocks_every_start_that_would_overlap_it_but_not_the_ones_touching_its_edges()
    {
        var series = new OpenSlotFinder.Busy(DayOfWeek.Tuesday, new TimeOnly(16, 0), 45);

        var slots = OpenSlotFinder.WeeklyFreeStarts([Window(DayOfWeek.Tuesday, 15, 18)], [series], 45);

        // 15:15 → 16:00'da biter (dokunur, çakışmaz); 16:45 → serinin bittiği an başlar.
        Assert.Equal(["Tuesday 15:00", "Tuesday 15:15", "Tuesday 16:45", "Tuesday 17:00", "Tuesday 17:15"], Format(slots));
    }

    [Fact]
    public void A_series_on_another_day_does_not_block_and_slots_are_ordered_monday_first()
    {
        var series = new OpenSlotFinder.Busy(DayOfWeek.Saturday, new TimeOnly(10, 0), 60);

        var slots = OpenSlotFinder.WeeklyFreeStarts(
            [Window(DayOfWeek.Saturday, 10, 11), Window(DayOfWeek.Monday, 10, 11)], [series], 60);

        Assert.Equal(["Monday 10:00"], Format(slots));
    }

    [Fact]
    public void A_window_shorter_than_the_lesson_yields_no_start()
    {
        Assert.Empty(OpenSlotFinder.WeeklyFreeStarts([Window(DayOfWeek.Friday, 15, 16)], [], 90));
    }

    [Fact]
    public void Overlapping_windows_on_the_same_day_do_not_produce_duplicate_starts()
    {
        var slots = OpenSlotFinder.WeeklyFreeStarts(
            [Window(DayOfWeek.Monday, 15, 16), Window(DayOfWeek.Monday, 15, 16)], [], 60);

        Assert.Equal(["Monday 15:00"], Format(slots));
    }

    [Fact]
    public void Non_positive_duration_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => OpenSlotFinder.WeeklyFreeStarts([Window(DayOfWeek.Monday, 15, 16)], [], 0));
    }

    [Fact]
    public void First_open_date_is_the_next_matching_weekday_when_nothing_blocks_it()
    {
        var slot = new OpenSlotFinder.Slot(DayOfWeek.Tuesday, new TimeOnly(16, 0));

        // 2026-10-08 Perşembe → ilk Salı 2026-10-13.
        var date = OpenSlotFinder.FirstOpenDate(slot, 45, new DateOnly(2026, 10, 8), 8, _ => false, []);

        Assert.Equal(new DateOnly(2026, 10, 13), date);
    }

    [Fact]
    public void First_open_date_skips_blocked_days_and_one_off_lessons_but_the_slot_stays_open()
    {
        var slot = new OpenSlotFinder.Slot(DayOfWeek.Tuesday, new TimeOnly(16, 0));
        var holiday = new DateOnly(2026, 10, 13);
        var makeup = (new DateOnly(2026, 10, 20), new TimeOnly(16, 30), new TimeOnly(17, 15));

        var date = OpenSlotFinder.FirstOpenDate(
            slot, 45, new DateOnly(2026, 10, 8), 8, d => d == holiday, [makeup]);

        Assert.Equal(new DateOnly(2026, 10, 27), date);
    }

    [Fact]
    public void First_open_date_is_null_when_every_week_in_the_horizon_is_blocked()
    {
        var slot = new OpenSlotFinder.Slot(DayOfWeek.Tuesday, new TimeOnly(16, 0));

        Assert.Null(OpenSlotFinder.FirstOpenDate(slot, 45, new DateOnly(2026, 10, 8), 4, _ => true, []));
    }

    [Fact]
    public void First_open_date_counts_the_start_day_itself()
    {
        var slot = new OpenSlotFinder.Slot(DayOfWeek.Thursday, new TimeOnly(16, 0));

        Assert.Equal(new DateOnly(2026, 10, 8),
            OpenSlotFinder.FirstOpenDate(slot, 45, new DateOnly(2026, 10, 8), 8, _ => false, []));
    }

    [Fact]
    public void First_open_date_skips_today_when_the_start_time_has_already_passed()
    {
        var slot = new OpenSlotFinder.Slot(DayOfWeek.Thursday, new TimeOnly(15, 0));
        var today = new DateOnly(2026, 10, 8);

        Assert.Equal(new DateOnly(2026, 10, 15),
            OpenSlotFinder.FirstOpenDate(slot, 45, today, 8, _ => false, [], notBeforeOnFrom: new TimeOnly(18, 20)));
        Assert.Equal(today,
            OpenSlotFinder.FirstOpenDate(slot, 45, today, 8, _ => false, [], notBeforeOnFrom: new TimeOnly(14, 0)));
    }
}
