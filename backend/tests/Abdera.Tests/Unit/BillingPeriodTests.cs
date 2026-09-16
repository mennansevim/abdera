using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Shared;

namespace Abdera.Tests.Unit;

public class BillingPeriodTests
{
    [Theory]
    [InlineData("2026-09", true)]
    [InlineData("2026-12", true)]
    [InlineData("2026-13", false)]
    [InlineData("2026-00", false)]
    [InlineData("2026-9", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValid_accepts_only_yyyy_MM(string? period, bool expected) =>
        Assert.Equal(expected, BillingPeriod.IsValid(period));

    [Fact]
    public void Parse_throws_validation_error_with_field_name()
    {
        var error = Assert.Throws<ValidationFailedException>(() => BillingPeriod.Parse("eylul", "startPeriod"));

        Assert.True(error.Errors.ContainsKey("startPeriod"));
    }

    [Fact]
    public void Sequence_walks_across_the_year_boundary()
    {
        var periods = BillingPeriod.Sequence("2026-11", 4);

        Assert.Equal(["2026-11", "2026-12", "2027-01", "2027-02"], periods);
    }

    [Fact]
    public void DueDate_uses_configured_day_and_never_exceeds_28()
    {
        Assert.Equal(new DateOnly(2027, 2, 15), BillingPeriod.DueDate("2027-02", 15));
        Assert.Equal(new DateOnly(2027, 2, 28), BillingPeriod.DueDate("2027-02", 31));
    }
}
