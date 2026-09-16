namespace Abdera.Api.Modules.Billing.Domain;

// Aidat hesabının tamamı. Saf fonksiyon - DbContext'e, saate, kültüre bağımlı değil;
// CLAUDE.md test stratejisi: "ücret hesaplama -> saf birim testi, gerçek veritabanı gerekmez".
//
// İki bağımsız indirim ekseni var ve bilinçli olarak farklı davranırlar:
//
//   1. ÖĞRENCİ İNDİRİMİ (çoklu kurs / kardeş) - aylık tutarı düşürür. Karar: birden
//      fazlası uygulanabiliyorsa EN YÜKSEĞİ alınır, toplanmaz (%5 + %5 = %5). Veliye
//      anlatması en kolay ve sürpriz indirim üretmeyen kural.
//      Elle girilen indirim (Enrollment.ManualDiscountPercent) bu kuralın YERİNE geçer -
//      admin bilinçli bir karar vermiştir, otomatik kural onu ezmemeli.
//
//   2. PEŞİN ÖDEME İNDİRİMİ - yıl başı toplu ödeme kampanyası. Öğrenci indiriminin
//      ÜSTÜNE uygulanır, çünkü farklı bir şeyin karşılığı: biri kimin olduğuyla,
//      diğeri ne zaman ödendiğiyle ilgili. Veliye giden duyuruda da ayrı maddeler.
public static class TuitionCalculator
{
    public readonly record struct DiscountContext(
        bool AttendsMultipleCourses,
        bool HasSibling,
        decimal? ManualPercent,
        string? ManualReason,
        decimal MultiCoursePercent,
        decimal SiblingPercent);

    public record Breakdown(decimal BaseAmount, decimal DiscountPercent, string? DiscountReason, decimal NetAmount);

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // Tek bir ayın aidatı - peşin ödeme söz konusu değilken kullanılır.
    public static Breakdown ComputeMonthly(decimal baseAmount, DiscountContext context)
    {
        var (percent, reason) = StudentDiscount(context);
        return Build(baseAmount, percent, reason);
    }

    // Peşin ödeme kampanyasında bir ayın aidatı: öğrenci indirimi + peşin indirimi.
    public static Breakdown ComputePrepaidMonthly(decimal baseAmount, DiscountContext context, decimal prepayPercent)
    {
        var (studentPercent, studentReason) = StudentDiscount(context);
        if (prepayPercent <= 0) return Build(baseAmount, studentPercent, studentReason);

        // Bileşik: %5 ve %10 birlikte %15 değil %14,5 eder. Toplayıp yazmak tutarı
        // yanlış hesaplardı - önce birini, sonra kalana diğerini uygularız.
        var combined = Round(100m - (100m - studentPercent) * (100m - prepayPercent) / 100m);
        var prepayReason = $"Peşin ödeme indirimi (%{Trim(prepayPercent)})";
        var reason = studentReason is null ? prepayReason : $"{studentReason} + {prepayReason}";
        return Build(baseAmount, combined, reason);
    }

    // Verilen ay sayısı için geçerli kademe: MinMonths'u aşmayan kademeler arasından
    // en yükseği. Hiçbir kademe uymuyorsa indirim yok (örn. 2 ay peşin ödeyene indirim yok).
    public static decimal ResolvePrepayPercent(IEnumerable<PrepayDiscountTier> tiers, int months) =>
        tiers.Where(tier => tier.MinMonths <= months)
            .Select(tier => tier.Percent)
            .DefaultIfEmpty(0m)
            .Max();

    // Öğrenci indirimi tek başına - peşin ödeme ekranı "bu öğrenci zaten %5 alıyor"
    // bilgisini taban tutardan bağımsız göstermek için kullanır.
    public static (decimal Percent, string? Reason) StudentDiscount(DiscountContext context)
    {
        if (context.ManualPercent is { } manual && manual > 0)
        {
            var reason = string.IsNullOrWhiteSpace(context.ManualReason)
                ? $"Özel indirim (%{Trim(manual)})"
                : $"{context.ManualReason.Trim()} (%{Trim(manual)})";
            return (Round(manual), reason);
        }

        var candidates = new List<(decimal Percent, string Reason)>();
        if (context.AttendsMultipleCourses && context.MultiCoursePercent > 0)
            candidates.Add((context.MultiCoursePercent, $"2 kurs indirimi (%{Trim(context.MultiCoursePercent)})"));
        if (context.HasSibling && context.SiblingPercent > 0)
            candidates.Add((context.SiblingPercent, $"Kardeş indirimi (%{Trim(context.SiblingPercent)})"));

        if (candidates.Count == 0) return (0m, null);

        // "En yüksek olan uygulanır" - eşitlikte ilk sıradaki (çoklu kurs) kazanır,
        // böylece sonuç sıralamadan bağımsız ve tekrarlanabilir olur.
        var best = candidates.OrderByDescending(candidate => candidate.Percent).First();
        return (Round(best.Percent), best.Reason);
    }

    private static Breakdown Build(decimal baseAmount, decimal percent, string? reason)
    {
        var rounded = Round(baseAmount);
        if (percent <= 0) return new Breakdown(rounded, 0m, null, rounded);

        var net = Round(rounded * (100m - percent) / 100m);
        return new Breakdown(rounded, percent, reason, net);
    }

    // "%5" yazmak için - 5,00 yerine 5, 7,50 yerine 7,5. Kültürden bağımsız olsun diye
    // InvariantCulture ile biçimlenir, sonra ondalık ayırıcı Türkçeye çevrilir
    // (CLAUDE.md: veliye görünecek metin Türkçe, ama named-culture çağrısı Alpine'da riskli).
    private static string Trim(decimal percent) =>
        percent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
}
