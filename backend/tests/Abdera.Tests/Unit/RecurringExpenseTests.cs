using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

// Sabit aylık gider kalemi (docs/10-decisions.md M9): bir kez girilen tutar her aya sayılır,
// değişiklik seçilen aydan itibaren geçerli olur ve geçmiş ayların tutarı hiç değişmez.
public class RecurringExpenseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 10, 0, 0, TimeSpan.Zero);

    private static RecurringExpense Rent(decimal amount = 20000m, int year = 2026, int month = 1) =>
        RecurringExpense.Create(ExpenseCategory.Rent, "Kira", amount, "TRY", new DateOnly(year, month, 1), null, null, Now);

    [Fact]
    public void Amount_counts_for_every_month_from_start_until_ended()
    {
        var rent = Rent();

        Assert.Equal(0m, rent.AmountFor(new DateOnly(2025, 12, 1)));
        Assert.Equal(20000m, rent.AmountFor(new DateOnly(2026, 1, 1)));
        Assert.Equal(20000m, rent.AmountFor(new DateOnly(2026, 12, 15)));
        Assert.Equal(20000m, rent.AmountFor(new DateOnly(2030, 6, 1)));
    }

    [Fact]
    public void Changing_amount_applies_from_the_chosen_month_and_keeps_past_months()
    {
        var rent = Rent();

        rent.ChangeAmount(25000m, "TRY", new DateOnly(2026, 7, 1), null, Now);

        Assert.Equal(20000m, rent.AmountFor(new DateOnly(2026, 6, 1)));
        Assert.Equal(25000m, rent.AmountFor(new DateOnly(2026, 7, 1)));
        Assert.Equal(25000m, rent.AmountFor(new DateOnly(2027, 1, 1)));
        var first = rent.EffectiveAmounts.First();
        Assert.Equal(new DateOnly(2026, 6, 30), first.EffectiveUntil);
        Assert.Equal(2, rent.EffectiveAmounts.Count());
    }

    [Fact]
    public void Change_before_the_current_amount_started_is_rejected()
    {
        var rent = Rent(month: 6);

        Assert.Throws<ConflictException>(() => rent.ChangeAmount(25000m, "TRY", new DateOnly(2026, 5, 1), null, Now));
        Assert.Equal(20000m, rent.AmountFor(new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Same_month_change_is_a_correction_that_keeps_the_old_row_but_stops_counting_it()
    {
        var rent = Rent(month: 9);

        rent.ChangeAmount(21000m, "TRY", new DateOnly(2026, 9, 1), null, Now);

        Assert.Equal(21000m, rent.AmountFor(new DateOnly(2026, 9, 1)));
        Assert.Equal(2, rent.Amounts.Count);
        Assert.Single(rent.EffectiveAmounts);
        Assert.NotNull(rent.Amounts.Single(amount => amount.MonthlyAmount == 20000m).SupersededAt);
    }

    [Fact]
    public void Ended_item_stops_counting_after_its_last_month_and_can_restart_later()
    {
        var rent = Rent();

        rent.EndAfter(new DateOnly(2026, 8, 1), Now);

        Assert.True(rent.IsEnded);
        Assert.Equal(20000m, rent.AmountFor(new DateOnly(2026, 8, 1)));
        Assert.Equal(0m, rent.AmountFor(new DateOnly(2026, 9, 1)));
        Assert.Throws<ConflictException>(() => rent.EndAfter(new DateOnly(2026, 9, 1), Now));
        Assert.Throws<ConflictException>(() => rent.ChangeAmount(30000m, "TRY", new DateOnly(2026, 8, 1), null, Now));

        rent.ChangeAmount(30000m, "TRY", new DateOnly(2027, 1, 1), null, Now);
        Assert.Equal(0m, rent.AmountFor(new DateOnly(2026, 12, 1)));
        Assert.Equal(30000m, rent.AmountFor(new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Non_positive_amount_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => Rent(amount: 0m));
    }
}
