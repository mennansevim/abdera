using Abdera.Api.Modules.Pricing.Domain;

namespace Abdera.Api.Modules.Billing.Domain;

// docs/03-erd.md - Billing > fee_plans. Bir Enrollment'ın hangi fiyat kalemine göre
// faturalandırılacağını sabitler - PriceListItem'dan snapshot alınır (docs/10-decisions.md A1),
// PriceList sonradan değişse bile bu FeePlan etkilenmez.
public class FeePlan
{
    public Guid Id { get; private set; }
    public Guid EnrollmentId { get; private set; }
    public Guid PriceListItemId { get; private set; }
    public BillingType BillingType { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public int? DueDay { get; private set; }
    public int? PackageLessonCount { get; private set; }
    public DateOnly ActiveFrom { get; private set; }
    public DateOnly? ActiveUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private FeePlan() { }

    public static FeePlan CreateFromPriceListItem(
        Guid enrollmentId, PriceListItem item, int? dueDay, DateOnly activeFrom, DateTimeOffset now)
    {
        if (item.BillingType == BillingType.Monthly && dueDay is null or < 1 or > 28)
            throw new ArgumentException("Aylık plan için ayın günü 1-28 arasında olmalı.", nameof(dueDay));

        return new FeePlan
        {
            Id = Guid.NewGuid(),
            EnrollmentId = enrollmentId,
            PriceListItemId = item.Id,
            BillingType = item.BillingType,
            Amount = item.Amount,
            Currency = item.Currency,
            DueDay = item.BillingType == BillingType.Monthly ? dueDay : null,
            PackageLessonCount = item.PackageLessonCount,
            ActiveFrom = activeFrom,
            CreatedAt = now,
        };
    }

    public bool IsActiveOn(DateOnly date) => ActiveFrom <= date && (ActiveUntil is null || date <= ActiveUntil);

    public void End(DateOnly activeUntil)
    {
        ActiveUntil = activeUntil;
    }
}
