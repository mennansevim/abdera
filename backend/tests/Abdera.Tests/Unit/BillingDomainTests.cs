using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Pricing.Domain;
using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

public class BillingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FeePlan_CreateFromPriceListItem_throws_when_monthly_missing_valid_due_day()
    {
        var item = PriceListItem.Create(Guid.NewGuid(), Guid.NewGuid(), 45, BillingType.Monthly, 2000m, "TRY", null);

        Assert.Throws<ArgumentException>(() => FeePlan.CreateFromPriceListItem(
            Guid.NewGuid(), item, dueDay: null, new DateOnly(2026, 9, 1), Now));
        Assert.Throws<ArgumentException>(() => FeePlan.CreateFromPriceListItem(
            Guid.NewGuid(), item, dueDay: 30, new DateOnly(2026, 9, 1), Now));
    }

    [Fact]
    public void FeePlan_CreateFromPriceListItem_snapshots_amount_and_currency()
    {
        var item = PriceListItem.Create(Guid.NewGuid(), Guid.NewGuid(), 45, BillingType.Monthly, 2000m, "TRY", null);
        var feePlan = FeePlan.CreateFromPriceListItem(Guid.NewGuid(), item, dueDay: 5, new DateOnly(2026, 9, 1), Now);

        Assert.Equal(2000m, feePlan.Amount);
        Assert.Equal("TRY", feePlan.Currency);
        Assert.Equal(5, feePlan.DueDay);

        // Kaynak kalem sonradan değişse bile snapshot etkilenmez (docs/10-decisions.md A1).
        item.ApplyPercentageChange(20);
        Assert.Equal(2000m, feePlan.Amount);
    }

    private static Receivable CreateUnpaidReceivable(decimal amount, DateOnly dueDate) =>
        Receivable.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "2026-09", amount, "TRY", dueDate, Now);

    [Fact]
    public void Receivable_RecordPaymentEffect_transitions_unpaid_to_partial_to_paid()
    {
        var receivable = CreateUnpaidReceivable(1000m, new DateOnly(2026, 9, 5));

        receivable.RecordPaymentEffect(400m, Now);
        Assert.Equal(ReceivableStatus.Partial, receivable.Status);

        receivable.RecordPaymentEffect(1000m, Now);
        Assert.Equal(ReceivableStatus.Paid, receivable.Status);
    }

    [Fact]
    public void Receivable_MarkOverdueIfPastDue_only_applies_to_unpaid_or_partial()
    {
        var paid = CreateUnpaidReceivable(1000m, new DateOnly(2026, 8, 1));
        paid.RecordPaymentEffect(1000m, Now);

        paid.MarkOverdueIfPastDue(new DateOnly(2026, 8, 19), Now);

        Assert.Equal(ReceivableStatus.Paid, paid.Status); // Paid asla Overdue'ya dönmez
    }

    [Fact]
    public void Receivable_MarkOverdueIfPastDue_promotes_unpaid_when_due_date_passed()
    {
        var receivable = CreateUnpaidReceivable(1000m, new DateOnly(2026, 8, 1));

        receivable.MarkOverdueIfPastDue(new DateOnly(2026, 8, 19), Now);

        Assert.Equal(ReceivableStatus.Overdue, receivable.Status);
    }

    [Fact]
    public void Receivable_MarkOverdueIfPastDue_does_nothing_before_due_date()
    {
        var receivable = CreateUnpaidReceivable(1000m, new DateOnly(2026, 9, 5));

        receivable.MarkOverdueIfPastDue(new DateOnly(2026, 8, 19), Now);

        Assert.Equal(ReceivableStatus.Unpaid, receivable.Status);
    }

    [Fact]
    public void Receivable_partial_payment_after_overdue_moves_to_partial_not_staying_overdue()
    {
        // docs/05-state-models.md: "OVERDUE -> PARTIAL: kısmi ödeme girildi"
        var receivable = CreateUnpaidReceivable(1000m, new DateOnly(2026, 8, 1));
        receivable.MarkOverdueIfPastDue(new DateOnly(2026, 8, 19), Now);
        Assert.Equal(ReceivableStatus.Overdue, receivable.Status);

        receivable.RecordPaymentEffect(300m, Now);

        Assert.Equal(ReceivableStatus.Partial, receivable.Status);
    }

    [Fact]
    public void Receivable_Cancel_throws_when_already_paid()
    {
        var receivable = CreateUnpaidReceivable(1000m, new DateOnly(2026, 9, 5));
        receivable.RecordPaymentEffect(1000m, Now);

        Assert.Throws<ConflictException>(() => receivable.Cancel(Now));
    }

    [Fact]
    public void Payment_Create_throws_when_amount_not_positive()
    {
        Assert.Throws<ArgumentException>(() => Payment.Create(
            Guid.NewGuid(), 0m, new DateOnly(2026, 9, 1), PaymentMethod.Cash, null, null, Guid.NewGuid(), Now));
    }

    [Fact]
    public void PaymentCorrection_preserves_before_and_after_amounts_and_requires_reason()
    {
        var paymentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var correction = PaymentCorrection.Create(paymentId, 1200m, 950m, "Dekont düzeltmesi", actorId, Now);

        Assert.Equal(paymentId, correction.PaymentId);
        Assert.Equal(1200m, correction.PreviousAmount);
        Assert.Equal(950m, correction.CorrectedAmount);
        Assert.Throws<ArgumentException>(() => PaymentCorrection.Create(paymentId, 950m, 900m, " ", actorId, Now));
        Assert.Throws<ArgumentException>(() => PaymentCorrection.Create(paymentId, 950m, 950m, "aynı", actorId, Now));
    }
}
