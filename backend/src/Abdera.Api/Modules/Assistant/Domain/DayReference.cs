namespace Abdera.Api.Modules.Assistant.Domain;

// Metindeki gün ifadesi: "perşembe" (Weekday), "yarın" (OffsetDays = 1), "haftaya salı"
// (Weekday + NextWeek). Takvim tarihine çevrilmesi okulun yerel "bugün"üne bağlı olduğu için
// ayrıştırmadan ayrı tutulur; ayrıştırıcı saatten bağımsız kalır ve test edilebilir.
public record DayReference(DayOfWeek? Weekday, int? OffsetDays, bool NextWeek)
{
    // Haftanın günü bugün de olabilir: perşembe günü sorulan "perşembe dersi" bugünkü derstir.
    public DateOnly Resolve(DateOnly today)
    {
        if (OffsetDays is { } offset) return today.AddDays(offset);

        var daysAhead = Weekday is { } weekday ? ((int)weekday - (int)today.DayOfWeek + 7) % 7 : 0;
        return today.AddDays(daysAhead + (NextWeek ? 7 : 0));
    }
}
