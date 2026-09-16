using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

public class BillingDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TuitionRate_EndOn_rejects_a_closing_date_before_the_start()
    {
        var rate = TuitionRate.Create(CourseKind.Individual, 4, 6000m, "TRY", new DateOnly(2026, 9, 1), null, Now);

        Assert.Throws<ArgumentException>(() => rate.EndOn(new DateOnly(2026, 8, 31)));
    }

    [Fact]
    public void TuitionRate_IsActiveOn_respects_an_open_ended_range()
    {
        var rate = TuitionRate.Create(CourseKind.Group, 4, 4500m, "TRY", new DateOnly(2026, 9, 1), null, Now);

        Assert.False(rate.IsActiveOn(new DateOnly(2026, 8, 31)));
        Assert.True(rate.IsActiveOn(new DateOnly(2026, 9, 1)));
        Assert.True(rate.IsActiveOn(new DateOnly(2030, 1, 1)));

        rate.EndOn(new DateOnly(2027, 8, 31));
        Assert.False(rate.IsActiveOn(new DateOnly(2027, 9, 1)));
    }

    private static Receivable CreateUnpaidReceivable(decimal amount, DateOnly dueDate) =>
        Receivable.Create(
            Guid.NewGuid(), Guid.NewGuid(), "2026-09",
            new TuitionCalculator.Breakdown(amount, 0m, null, amount), "TRY", dueDate, Now);

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
    public void Receivable_Reprice_rewrites_the_whole_breakdown_while_unpaid()
    {
        var receivable = CreateUnpaidReceivable(6000m, new DateOnly(2026, 9, 1));
        var planId = Guid.NewGuid();

        receivable.Reprice(new TuitionCalculator.Breakdown(6000m, 14.5m, "Peşin", 5130m), planId, Now);

        Assert.Equal(6000m, receivable.BaseAmount);
        Assert.Equal(14.5m, receivable.DiscountPercent);
        Assert.Equal(5130m, receivable.Amount);
        Assert.Equal(planId, receivable.PrepayPlanId);
    }

    [Fact]
    public void Receivable_Reprice_is_refused_once_money_has_been_recorded()
    {
        // Tahsil edilmiş parayla tutarsız bir tutar yazmak, eksik/fazla bakiyeyi
        // sessizce gizlerdi - peşin ödeme kampanyası bu aya dokunamaz.
        var partiallyPaid = CreateUnpaidReceivable(6000m, new DateOnly(2026, 9, 1));
        partiallyPaid.RecordPaymentEffect(1000m, Now);
        Assert.Throws<ConflictException>(() => partiallyPaid.Reprice(
            new TuitionCalculator.Breakdown(6000m, 10m, "Peşin", 5400m), Guid.NewGuid(), Now));

        var paid = CreateUnpaidReceivable(6000m, new DateOnly(2026, 9, 1));
        paid.RecordPaymentEffect(6000m, Now);
        Assert.Throws<ConflictException>(() => paid.Reprice(
            new TuitionCalculator.Breakdown(6000m, 10m, "Peşin", 5400m), Guid.NewGuid(), Now));

        var cancelled = CreateUnpaidReceivable(6000m, new DateOnly(2026, 9, 1));
        cancelled.Cancel(Now);
        Assert.Throws<ConflictException>(() => cancelled.Reprice(
            new TuitionCalculator.Breakdown(6000m, 10m, "Peşin", 5400m), Guid.NewGuid(), Now));
    }

    [Fact]
    public void Enrollment_SetManualDiscount_rejects_out_of_range_percentages()
    {
        var enrollment = Enrollment.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CourseKind.Individual, new DateOnly(2026, 9, 1), Now);

        Assert.Throws<ArgumentOutOfRangeException>(() => enrollment.SetManualDiscount(-1m, null, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => enrollment.SetManualDiscount(101m, null, Now));

        enrollment.SetManualDiscount(15m, "  Burslu  ", Now);
        Assert.Equal(15m, enrollment.ManualDiscountPercent);
        Assert.Equal("Burslu", enrollment.ManualDiscountReason);

        // null'a çekmek gerekçeyi de temizler - otomatik kurallara geri döner.
        enrollment.SetManualDiscount(null, "Burslu", Now);
        Assert.Null(enrollment.ManualDiscountPercent);
        Assert.Null(enrollment.ManualDiscountReason);
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
