using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Infrastructure;

namespace Abdera.Tests.Unit;

// Aylık aidatların dönemlere dağılımı - docs/10-decisions.md AD14, AD16. Her 4 ders bir ödeme:
// alınmış ödemeler en erken dönemleri karşılar, ödeme görmüş D00n kendi dönemini korur.
public class CycleCoverageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);

    private static Receivable Due(string period) =>
        Receivable.Create(Guid.NewGuid(), Guid.NewGuid(), period, new TuitionCalculator.Breakdown(6000m, 0m, null, 6000m), "TRY", new DateOnly(2026, 9, 1), Now);

    private static Receivable Cycle(int number) =>
        Receivable.CreateCycle(Guid.NewGuid(), Guid.NewGuid(), number, new TuitionCalculator.Breakdown(6000m, 0m, null, 6000m), "TRY",
            new DateOnly(2026, 10, 29), Now, new DateOnly(2026, 10, 22), null);

    [Fact]
    public void A_paid_later_month_covers_the_first_cycle_before_an_unpaid_earlier_one()
    {
        var september = Due("2026-09");
        var october = Due("2026-10");

        var coverage = CycleReceivableAccruer.AssignCoverage([september, october], new Dictionary<int, Receivable>(),
            new Dictionary<Guid, decimal> { [october.Id] = 6000m });

        Assert.Same(october, coverage[1]);
        Assert.Same(september, coverage[2]);
    }

    [Fact]
    public void Months_cover_in_order_and_an_unpaid_cycle_due_gives_way()
    {
        var september = Due("2026-09");
        var october = Due("2026-10");
        var d002 = Cycle(2);

        var coverage = CycleReceivableAccruer.AssignCoverage([september, october], new Dictionary<int, Receivable> { [2] = d002 },
            new Dictionary<Guid, decimal> { [september.Id] = 6000m });

        Assert.Same(september, coverage[1]);
        Assert.Same(october, coverage[2]);
    }

    [Fact]
    public void A_paid_cycle_due_keeps_its_cycle_and_the_month_moves_on()
    {
        var september = Due("2026-09");
        var october = Due("2026-10");
        var d002 = Cycle(2);

        var coverage = CycleReceivableAccruer.AssignCoverage([september, october], new Dictionary<int, Receivable> { [2] = d002 },
            new Dictionary<Guid, decimal> { [september.Id] = 6000m, [october.Id] = 6000m, [d002.Id] = 6000m });

        Assert.Same(september, coverage[1]);
        Assert.False(coverage.ContainsKey(2));
        Assert.Same(october, coverage[3]);
    }
}
