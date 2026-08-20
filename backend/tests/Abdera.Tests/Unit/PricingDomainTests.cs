using Abdera.Api.Modules.Pricing.Domain;

namespace Abdera.Tests.Unit;

public class PricingDomainTests
{
    [Fact]
    public void PriceListItem_Create_throws_when_package_has_no_lesson_count()
    {
        Assert.Throws<ArgumentException>(() => PriceListItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), 45, BillingType.Package, 1000m, "TRY", packageLessonCount: null));
    }

    [Fact]
    public void PriceListItem_Create_throws_when_monthly_has_lesson_count()
    {
        Assert.Throws<ArgumentException>(() => PriceListItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), 45, BillingType.Monthly, 2000m, "TRY", packageLessonCount: 8));
    }

    [Fact]
    public void PriceListItem_Create_throws_when_amount_negative()
    {
        Assert.Throws<ArgumentException>(() => PriceListItem.Create(
            Guid.NewGuid(), Guid.NewGuid(), 45, BillingType.Monthly, -100m, "TRY", null));
    }

    [Fact]
    public void PriceListItem_ApplyPercentageChange_rounds_to_two_decimals()
    {
        var item = PriceListItem.Create(Guid.NewGuid(), Guid.NewGuid(), 45, BillingType.Monthly, 2000m, "TRY", null);

        item.ApplyPercentageChange(15);

        Assert.Equal(2300m, item.Amount);
    }

    [Fact]
    public void PriceList_Create_throws_when_effective_until_before_from()
    {
        Assert.Throws<ArgumentException>(() => PriceList.Create(
            "2026 Sezonu", new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1), Guid.NewGuid(), DateTimeOffset.UtcNow));
    }
}
