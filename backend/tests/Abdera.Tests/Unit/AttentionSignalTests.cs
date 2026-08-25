using Abdera.Api.Modules.People.Domain;

namespace Abdera.Tests.Unit;

public class AttentionSignalTests
{
    [Fact]
    public void No_signal_is_emitted_for_a_single_absence_or_concern()
    {
        var signal = AttentionSignal.Evaluate(1, 1);

        Assert.False(signal.NeedsAttention);
        Assert.Empty(signal.Reasons);
    }

    [Fact]
    public void Explainable_reasons_are_emitted_at_thresholds()
    {
        var signal = AttentionSignal.Evaluate(3, 2);

        Assert.True(signal.NeedsAttention);
        Assert.Contains("Son 30 günde 3 devamsızlık", signal.Reasons);
        Assert.Contains("Son 4 yorumun 2 tanesinde dikkat işareti", signal.Reasons);
    }
}
