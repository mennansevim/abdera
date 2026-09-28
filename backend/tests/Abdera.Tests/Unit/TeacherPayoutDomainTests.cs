using Abdera.Api.Modules.Billing.Domain;

namespace Abdera.Tests.Unit;

// Öğretmenlere haftalık ders ödemesi (docs/10-decisions.md O1). Hafta sınırı ve tutar
// hesabı saf birim testiyle korunur - gerçek veritabanı gerekmez (docs/09-testing.md).
public class TeacherPayoutDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

    // Kullanıcı kuralı: "pazartesi dahil cumartesi dahil" - ödeme haftası pazartesi başlar,
    // cumartesi kapanır. Haftanın HER günü aynı haftaya düşmeli; pazar az önce kapanan haftaya
    // bağlanır (hiçbir haftanın ders aralığına girmez, aşağıdaki test).
    [Theory]
    [InlineData(2026, 9, 21)] // Pazartesi - haftanın ilk günü
    [InlineData(2026, 9, 24)] // Perşembe
    [InlineData(2026, 9, 26)] // Cumartesi - haftanın son günü
    [InlineData(2026, 9, 27)] // Pazar - kapanan haftaya
    public void Containing_maps_every_day_to_the_monday_to_saturday_week(int year, int month, int day)
    {
        var week = TeacherPayWeek.Containing(new DateOnly(year, month, day));

        Assert.Equal(new DateOnly(2026, 9, 21), week.Start);
        Assert.Equal(new DateOnly(2026, 9, 26), week.End);
        Assert.Equal(DayOfWeek.Monday, week.Start.DayOfWeek);
        Assert.Equal(DayOfWeek.Saturday, week.End.DayOfWeek);
    }

    // Ders aralığı [Start, ExclusiveEnd) = pazartesi 00:00 - pazar 00:00: cumartesi gecesi dahil,
    // pazar hiçbir haftada değil. Haftalar arasında çakışma olmamalı - aksi halde bir ders iki
    // ödemeye birden düşerdi.
    [Fact]
    public void Lesson_window_ends_before_sunday_and_weeks_never_overlap()
    {
        var week = TeacherPayWeek.Containing(new DateOnly(2026, 9, 24));

        Assert.Equal(DayOfWeek.Sunday, week.ExclusiveEnd.DayOfWeek);
        Assert.Equal(6, week.ExclusiveEnd.DayNumber - week.Start.DayNumber);
        Assert.Equal(week.ExclusiveEnd.AddDays(1), week.Next().Start);
        Assert.Equal(week.Start, week.Previous().ExclusiveEnd.AddDays(1));
    }

    [Fact]
    public void Weekly_amount_is_the_rate_times_completed_lessons()
    {
        var rate = TeacherPayRate.Create(Guid.NewGuid(), 450m, "TRY", null, Now);

        Assert.Equal(1350m, rate.ComputeWeeklyAmount(3));
        Assert.Equal(0m, rate.ComputeWeeklyAmount(0));
    }

    // Kuruşlu ücretlerde yuvarlama tek yerde (TeacherPayRate) yapılır ki gider satırı ile
    // ödeme satırı asla bir kuruş ayrışmasın.
    [Fact]
    public void Weekly_amount_is_rounded_to_two_decimals()
    {
        var rate = TeacherPayRate.Create(Guid.NewGuid(), 333.335m, "TRY", null, Now);

        Assert.Equal(1000.01m, rate.ComputeWeeklyAmount(3));
    }

    [Fact]
    public void A_rate_must_be_positive()
    {
        Assert.Throws<ArgumentException>(() => TeacherPayRate.Create(Guid.NewGuid(), 0m, "TRY", null, Now));
        Assert.Throws<ArgumentException>(() => TeacherPayRate.Create(Guid.NewGuid(), -1m, "TRY", null, Now));
    }

    [Fact]
    public void Changing_the_rate_keeps_the_row_and_records_the_new_amount()
    {
        var rate = TeacherPayRate.Create(Guid.NewGuid(), 400m, "TRY", null, Now);

        rate.ChangeAmount(500m, "TRY", Guid.NewGuid(), Now.AddDays(1));

        Assert.Equal(500m, rate.AmountPerLesson);
        Assert.Equal(Now.AddDays(1), rate.UpdatedAt);
        Assert.Equal(Now, rate.CreatedAt);
    }

    // Dersi olmayan bir haftaya ödeme satırı açılamaz - boş bir gider kaydı üretirdi.
    [Fact]
    public void A_payout_needs_at_least_one_completed_lesson()
    {
        var week = TeacherPayWeek.Containing(new DateOnly(2026, 9, 24));

        Assert.Throws<ArgumentOutOfRangeException>(() => TeacherWeeklyPayout.Create(
            Guid.NewGuid(), week, 0, 450m, 0m, 0m, "TRY", week.End, null, Guid.NewGuid(), null, Now));
    }

    // Fiyat snapshot'ının öğretmen tarafı: ödendikten sonra ücret değişse de satır kendi
    // hesabını taşımaya devam eder.
    [Fact]
    public void A_payout_freezes_the_whole_calculation_on_its_own_row()
    {
        var week = TeacherPayWeek.Containing(new DateOnly(2026, 9, 24));
        var expenseId = Guid.NewGuid();

        var payout = TeacherWeeklyPayout.Create(
            Guid.NewGuid(), week, 4, 450m, 1800m, 1800m, "try", week.End, "  elden  ", expenseId, null, Now);

        Assert.Equal(week.Start, payout.WeekStart);
        Assert.Equal(week.End, payout.WeekEnd);
        Assert.Equal(4, payout.LessonCount);
        Assert.Equal(450m, payout.RatePerLesson);
        Assert.Equal(1800m, payout.ComputedAmount);
        Assert.Equal("TRY", payout.Currency);
        Assert.Equal("elden", payout.Note);
        Assert.Equal(expenseId, payout.ExpenseId);
    }
}
