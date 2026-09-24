using Abdera.Api.Modules.Progress.Domain;

namespace Abdera.Tests.Unit;

// Kullanıcı kuralı: ilk yapay zekâ yorumu 4 not girilince üretilir, sonrasında en fazla ayda
// bir yenilenir; arada kayıtlı yorum gösterilir. Ay, okulun yerel takvimine göre sayılır.
public class ProgressSummaryPolicyTests
{
    private static DateTimeOffset Istanbul(DateTimeOffset instant) => instant.ToOffset(TimeSpan.FromHours(3));

    private static ProgressSummary Cached(DateTimeOffset generatedAt, int noteCount, DateTimeOffset latestNoteAt) =>
        ProgressSummary.Create(Guid.NewGuid(), null, "Yorum", noteCount, latestNoteAt, "model", generatedAt);

    [Theory]
    [InlineData(1, ProgressSummaryDecision.NotEnoughNotes)]
    [InlineData(3, ProgressSummaryDecision.NotEnoughNotes)]
    [InlineData(4, ProgressSummaryDecision.Generate)]
    [InlineData(9, ProgressSummaryDecision.Generate)]
    public void First_summary_needs_four_notes(int noteCount, ProgressSummaryDecision expected)
    {
        var now = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, ProgressSummary.Decide(null, noteCount, now, now, Istanbul));
    }

    [Fact]
    public void New_notes_in_the_same_month_keep_the_saved_summary()
    {
        var latest = new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
        var cached = Cached(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero), 4, latest);

        var decision = ProgressSummary.Decide(cached, 6, latest.AddDays(20), new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero), Istanbul);

        Assert.Equal(ProgressSummaryDecision.ServeCached, decision);
    }

    [Fact]
    public void A_new_month_with_new_notes_regenerates_once()
    {
        var latest = new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
        var cached = Cached(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero), 4, latest);

        var decision = ProgressSummary.Decide(cached, 5, latest.AddDays(30), new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero), Istanbul);

        Assert.Equal(ProgressSummaryDecision.Generate, decision);
    }

    [Fact]
    public void A_new_month_without_new_notes_does_not_call_the_provider()
    {
        var latest = new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
        var cached = Cached(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero), 4, latest);

        var decision = ProgressSummary.Decide(cached, 4, latest, new DateTimeOffset(2027, 1, 5, 9, 0, 0, TimeSpan.Zero), Istanbul);

        Assert.Equal(ProgressSummaryDecision.ServeCached, decision);
    }

    // 30 Eylül 22:30 UTC İstanbul'da 1 Ekim 01:30'dur: yorum Ekim'de üretilmiş sayılır ve Ekim
    // içinde tekrar üretilmez. UTC'ye göre saysaydık aynı ay içinde iki kez çağrılırdı.
    [Fact]
    public void Month_is_counted_in_school_local_time()
    {
        var generatedAt = new DateTimeOffset(2026, 9, 30, 22, 30, 0, TimeSpan.Zero);
        var cached = Cached(generatedAt, 4, generatedAt);

        Assert.Equal(ProgressSummaryDecision.ServeCached,
            ProgressSummary.Decide(cached, 7, generatedAt.AddDays(10), new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero), Istanbul));
        Assert.Equal(new DateOnly(2026, 11, 1), cached.NextRefreshOn(Istanbul));
    }
}
