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

    [Fact]
    public void Rsvp_signals_fire_at_two_not_attending_or_three_late_answers()
    {
        Assert.False(AttentionSignal.Evaluate(new AttentionSignal.Inputs(0, RecentNotAttendingRsvpCount: 1, RecentLateRsvpCount: 2)).NeedsAttention);

        var signal = AttentionSignal.Evaluate(new AttentionSignal.Inputs(0, RecentNotAttendingRsvpCount: 2, RecentLateRsvpCount: 3));
        Assert.Contains("Son 4 haftada 2 kez \"gelemiyorum\" dedi", signal.Reasons);
        Assert.Contains("Son 4 haftada 3 kez \"geç kalacağım\" dedi", signal.Reasons);
    }

    [Fact]
    public void Practice_signal_fires_only_when_a_regular_journal_goes_quiet()
    {
        // Günlüğü hiç tutmayan aile: sinyal yok.
        Assert.False(AttentionSignal.Evaluate(new AttentionSignal.Inputs(0, PracticeEntriesRecent: 0, PracticeEntriesPrior: 0)).NeedsAttention);
        // Hâlâ giriyor: sinyal yok.
        Assert.False(AttentionSignal.Evaluate(new AttentionSignal.Inputs(0, PracticeEntriesRecent: 1, PracticeEntriesPrior: 5)).NeedsAttention);
        // Bir kez girip bırakmış: düzenli sayılmaz.
        Assert.False(AttentionSignal.Evaluate(new AttentionSignal.Inputs(0, PracticeEntriesRecent: 0, PracticeEntriesPrior: 1)).NeedsAttention);

        var signal = AttentionSignal.Evaluate(new AttentionSignal.Inputs(0, PracticeEntriesRecent: 0, PracticeEntriesPrior: 2));
        Assert.Equal(["3 haftadır pratik günlüğü girilmedi (önceden düzenliydi)"], signal.Reasons);
    }

    [Fact]
    public void Overdue_signal_needs_three_months_and_is_skipped_when_the_viewer_cannot_see_money()
    {
        Assert.False(AttentionSignal.Evaluate(new AttentionSignal.Inputs(0, OverdueReceivableCount: 2)).NeedsAttention);
        Assert.False(AttentionSignal.Evaluate(new AttentionSignal.Inputs(0, OverdueReceivableCount: null)).NeedsAttention);

        var signal = AttentionSignal.Evaluate(new AttentionSignal.Inputs(0, OverdueReceivableCount: 3));
        Assert.Equal(["3 aylık aidat gecikmiş"], signal.Reasons);
    }

    [Fact]
    public void Skill_signal_lists_only_dropped_scores_in_one_reason()
    {
        var unchanged = AttentionSignal.Evaluate(new AttentionSignal.Inputs(0, SkillChanges:
            [new("Ritim", 3, 3), new("Teknik", 2, 4)]));
        Assert.False(unchanged.NeedsAttention);

        var signal = AttentionSignal.Evaluate(new AttentionSignal.Inputs(0, SkillChanges:
            [new("Ritim", 4, 3), new("Teknik", 2, 4), new("Nota okuma", 3, 1)]));
        Assert.Equal(["Yetenek puanı düştü: Ritim 4 → 3, Nota okuma 3 → 1"], signal.Reasons);
    }
}
