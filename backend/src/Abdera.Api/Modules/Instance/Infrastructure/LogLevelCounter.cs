using Serilog.Core;
using Serilog.Events;

namespace Abdera.Api.Modules.Instance.Infrastructure;

// Platformun gözlem paneli için son 24 saatin hata/uyarı SAYISI (docs/multi-tenant/ARCHITECTURE.md §3.3).
// Log metni tutulmaz ve platforma gitmez - yalnızca adet; metni operatör ajan üzerinden istek anında okur.
// Dakikalık 1440 kova bir halka oluşturur: eski dakikanın kovası yeniden kullanılırken sıfırlanır. Sayaç
// süreç belleğindedir, yeniden başlatmada sıfırlanır (CountingSince bunu gösterir).
public sealed class LogLevelCounter : ILogEventSink
{
    private const int Buckets = 24 * 60;
    private readonly long[] _minute = new long[Buckets];
    private readonly int[] _errors = new int[Buckets];
    private readonly int[] _warnings = new int[Buckets];
    private readonly Lock _gate = new();

    public DateTimeOffset CountingSince { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastErrorAt { get; private set; }

    public void Emit(LogEvent logEvent) => Record(logEvent.Level, logEvent.Timestamp);

    public void Record(LogEventLevel level, DateTimeOffset at)
    {
        if (level < LogEventLevel.Warning) return;
        var minute = at.ToUnixTimeSeconds() / 60;
        var slot = (int)(minute % Buckets);
        lock (_gate)
        {
            if (_minute[slot] != minute)
            {
                _minute[slot] = minute;
                _errors[slot] = 0;
                _warnings[slot] = 0;
            }
            if (level >= LogEventLevel.Error)
            {
                _errors[slot]++;
                if (LastErrorAt is null || at > LastErrorAt) LastErrorAt = at;
            }
            else _warnings[slot]++;
        }
    }

    public (int Errors, int Warnings) Last24Hours(DateTimeOffset now)
    {
        var current = now.ToUnixTimeSeconds() / 60;
        int errors = 0, warnings = 0;
        lock (_gate)
        {
            for (var slot = 0; slot < Buckets; slot++)
            {
                if (_minute[slot] <= current - Buckets || _minute[slot] > current) continue;
                errors += _errors[slot];
                warnings += _warnings[slot];
            }
        }
        return (errors, warnings);
    }
}
