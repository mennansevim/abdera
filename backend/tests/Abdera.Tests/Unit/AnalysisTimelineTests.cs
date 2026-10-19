using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Dashboard.Domain;
using Abdera.Api.Modules.Scheduling.Domain;

namespace Abdera.Tests.Unit;

public class AnalysisTimelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Periods_end_at_the_current_month_and_cross_year_boundaries()
    {
        var periods = AnalysisTimeline.Periods(new DateOnly(2026, 2, 14), 4);
        Assert.Equal(["2025-11", "2025-12", "2026-01", "2026-02"], periods);
    }

    [Theory]
    [InlineData(null, 6)]
    [InlineData(1, 3)]
    [InlineData(8, 8)]
    [InlineData(40, 12)]
    public void Month_count_is_clamped(int? requested, int expected) =>
        Assert.Equal(expected, AnalysisTimeline.NormalizeMonths(requested));

    [Fact]
    public void Cancelled_lesson_stays_cancelled_even_with_an_attendance_row() =>
        Assert.Equal(AnalysisLessonState.Cancelled,
            AnalysisTimeline.Classify(LessonStatus.Cancelled, AttendanceStatus.Present, Now.AddDays(-1), Now));

    [Fact]
    public void Attended_makeup_lesson_is_its_own_bucket_and_counts_as_attended()
    {
        var state = AnalysisTimeline.Classify(LessonStatus.Makeup, AttendanceStatus.Present, Now.AddDays(-1), Now);
        Assert.Equal(AnalysisLessonState.Makeup, state);
        Assert.True(AnalysisTimeline.IsAttended(state));
    }

    [Fact]
    public void Lesson_without_attendance_is_unmarked_in_the_past_and_upcoming_in_the_future()
    {
        Assert.Equal(AnalysisLessonState.Unmarked, AnalysisTimeline.Classify(LessonStatus.Normal, null, Now.AddHours(-1), Now));
        Assert.Equal(AnalysisLessonState.Upcoming, AnalysisTimeline.Classify(LessonStatus.Normal, null, Now.AddHours(1), Now));
    }

    [Fact]
    public void Rate_ignores_cancelled_unmarked_and_upcoming_lessons()
    {
        AnalysisLessonState[] states =
        [
            AnalysisLessonState.Present, AnalysisLessonState.Makeup, AnalysisLessonState.Excused,
            AnalysisLessonState.Absent, AnalysisLessonState.Cancelled, AnalysisLessonState.Unmarked,
            AnalysisLessonState.Upcoming,
        ];
        var attended = states.Count(AnalysisTimeline.IsAttended);
        var rated = states.Count(AnalysisTimeline.CountsTowardRate);
        Assert.Equal(50, AnalysisTimeline.Rate(attended, rated));
    }

    [Fact]
    public void Rate_is_null_without_any_rated_lesson() => Assert.Null(AnalysisTimeline.Rate(0, 0));

    [Fact]
    public void Combined_billing_status_puts_overdue_first_and_partial_before_unpaid()
    {
        Assert.Equal(ReceivableStatus.Overdue, AnalysisTimeline.CombineBillingStatus([ReceivableStatus.Paid, ReceivableStatus.Overdue]));
        Assert.Equal(ReceivableStatus.Paid, AnalysisTimeline.CombineBillingStatus([ReceivableStatus.Paid, ReceivableStatus.Cancelled]));
        Assert.Equal(ReceivableStatus.Partial, AnalysisTimeline.CombineBillingStatus([ReceivableStatus.Paid, ReceivableStatus.Unpaid]));
        Assert.Equal(ReceivableStatus.Unpaid, AnalysisTimeline.CombineBillingStatus([ReceivableStatus.Unpaid]));
    }
}
