using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;

namespace Abdera.Tests.Unit;

// Dönem (4 ders) aidatının saf kuralları - docs/10-decisions.md AD.
public class BillingCycleTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(8, 2)]
    [InlineData(9, 3)]
    public void The_first_lesson_of_a_cycle_opens_it(int countedLessons, int expectedCycles) =>
        Assert.Equal(expectedCycles, BillingCycle.RequiredCycles(countedLessons));

    [Fact]
    public void Cycle_keys_round_trip_and_sort_in_order()
    {
        Assert.Equal("D003", BillingCycle.Key(3));
        Assert.Equal(12, BillingCycle.Number("D012"));
        Assert.True(string.CompareOrdinal(BillingCycle.Key(9), BillingCycle.Key(10)) < 0);
        Assert.False(BillingCycle.IsKey("2026-10"));
        Assert.False(BillingCycle.IsKey("D000"));
        Assert.Throws<ArgumentOutOfRangeException>(() => BillingCycle.Key(0));
    }

    [Fact]
    public void Monthly_dues_are_replaced_only_for_individual_lessons_from_the_start_month_on()
    {
        var start = new DateOnly(2026, 11, 1);
        Assert.False(BillingCycle.ReplacesMonthly(CourseKind.Individual, start, "2026-10"));
        Assert.True(BillingCycle.ReplacesMonthly(CourseKind.Individual, start, "2026-11"));
        Assert.False(BillingCycle.ReplacesMonthly(CourseKind.Group, start, "2026-12"));
        Assert.False(BillingCycle.ReplacesMonthly(CourseKind.Individual, null, "2026-12"));
    }

    [Fact]
    public void A_cycle_due_falls_into_the_month_of_its_opening_lesson_and_a_prepaid_one_into_none()
    {
        Assert.Equal("2026-11", BillingCycle.AccrualMonth("D002", new DateOnly(2026, 11, 28)));
        Assert.Null(BillingCycle.AccrualMonth("D003", null));
        Assert.Equal("2026-10", BillingCycle.AccrualMonth("2026-10", null));
        Assert.Equal("3.", BillingCycle.MessageLabel("D003"));
    }
}
