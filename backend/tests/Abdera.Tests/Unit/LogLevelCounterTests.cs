using Abdera.Api.Modules.Instance.Infrastructure;
using Serilog.Events;

namespace Abdera.Tests.Unit;

// Gözlem paneli için hata/uyarı sayacı: yalnızca son 24 saat, bilgi seviyesi sayılmaz.
public class LogLevelCounterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Counts_errors_and_warnings_of_the_last_24_hours_only()
    {
        var counter = new LogLevelCounter();
        counter.Record(LogEventLevel.Information, Now);
        counter.Record(LogEventLevel.Warning, Now.AddMinutes(-5));
        counter.Record(LogEventLevel.Error, Now.AddHours(-23));
        counter.Record(LogEventLevel.Fatal, Now.AddMinutes(-1));
        // 24 saatten eski: aynı halka kovasına düşen yeni dakika onu siler, sayılmaz.
        counter.Record(LogEventLevel.Error, Now.AddHours(-25));

        Assert.Equal((2, 1), counter.Last24Hours(Now));
        Assert.Equal(Now.AddMinutes(-1), counter.LastErrorAt);
        // Bir gün sonra hepsi pencereden çıkar.
        Assert.Equal((0, 0), counter.Last24Hours(Now.AddDays(1).AddMinutes(1)));
    }

    [Fact]
    public void A_reused_bucket_forgets_the_previous_day()
    {
        var counter = new LogLevelCounter();
        counter.Record(LogEventLevel.Error, Now.AddDays(-1));
        counter.Record(LogEventLevel.Warning, Now);
        Assert.Equal((0, 1), counter.Last24Hours(Now));
    }
}
