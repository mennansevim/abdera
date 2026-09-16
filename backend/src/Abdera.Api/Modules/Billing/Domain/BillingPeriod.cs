using System.Text.RegularExpressions;
using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Billing.Domain;

// "2026-09" dönem etiketinin tek doğrulama/dönüştürme yeri. Önceden aynı regex ve aynı
// int.Parse(period[..4]) satırları dört ayrı dosyada kopyalanmıştı; biri düzeltilip
// diğerleri unutulduğunda sessizce farklı davranıyorlardı.
public static partial class BillingPeriod
{
    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? period) => !string.IsNullOrWhiteSpace(period) && Pattern().IsMatch(period);

    public static (int Year, int Month) Parse(string? period, string field = "period")
    {
        if (!IsValid(period))
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                [field] = ["Dönem 'yyyy-MM' biçiminde olmalı (örn. 2026-09)."],
            });

        return (int.Parse(period![..4]), int.Parse(period[5..]));
    }

    public static string Format(int year, int month) => $"{year:D4}-{month:D2}";

    // Dönemin ilk günü - tarife hangi dönemde yürürlükte diye bakarken kullanılır.
    public static DateOnly FirstDay(string period)
    {
        var (year, month) = Parse(period);
        return new DateOnly(year, month, 1);
    }

    public static DateOnly DueDate(string period, int dueDayOfMonth)
    {
        var (year, month) = Parse(period);
        return new DateOnly(year, month, Math.Clamp(dueDayOfMonth, 1, 28));
    }

    // Peşin ödemede ardışık ayları üretir: ("2026-09", 3) -> 2026-09, 2026-10, 2026-11.
    public static IReadOnlyList<string> Sequence(string startPeriod, int months)
    {
        if (months < 1) throw new ArgumentOutOfRangeException(nameof(months), "Ay sayısı en az 1 olmalı.");

        var (year, month) = Parse(startPeriod, "startPeriod");
        var first = new DateOnly(year, month, 1);
        return Enumerable.Range(0, months)
            .Select(offset => first.AddMonths(offset))
            .Select(date => Format(date.Year, date.Month))
            .ToList();
    }
}
