namespace Abdera.Api.Modules.Assistant.Domain;

// Metindeki gün ifadesi: "perşembe" (Weekday), "yarın" (OffsetDays = 1), "haftaya salı"
// (Weekday + NextWeek), "4 Kasım" (Month + DayOfMonth). Takvim tarihine çevrilmesi okulun
// yerel "bugün"üne bağlı olduğu için ayrıştırmadan ayrı tutulur; ayrıştırıcı saatten bağımsız
// kalır ve test edilebilir.
public record DayReference(DayOfWeek? Weekday, int? OffsetDays, bool NextWeek, int? Month = null, int? DayOfMonth = null)
{
    // Haftanın günü bugün de olabilir: perşembe günü sorulan "perşembe dersi" bugünkü derstir.
    // Ay ve gün verilmişse en yakın gelecekteki o tarih: geçmişte kaldıysa gelecek yılın.
    public DateOnly Resolve(DateOnly today)
    {
        if (Month is { } month && DayOfMonth is { } dayOfMonth)
        {
            var thisYear = new DateOnly(today.Year, month, Math.Min(dayOfMonth, DateTime.DaysInMonth(today.Year, month)));
            return thisYear >= today ? thisYear : new DateOnly(today.Year + 1, month, Math.Min(dayOfMonth, DateTime.DaysInMonth(today.Year + 1, month)));
        }

        if (OffsetDays is { } offset) return today.AddDays(offset);

        var daysAhead = Weekday is { } weekday ? ((int)weekday - (int)today.DayOfWeek + 7) % 7 : 0;
        return today.AddDays(daysAhead + (NextWeek ? 7 : 0));
    }
}
