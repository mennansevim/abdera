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

    // Tahsilatta elle girilen tutar (kullanıcı isteği: "ödenecek rakamı o anda editleyebileyim,
    // küsüratlar değişebiliyor"). Hesap yine sunucuda yapılır; yönetici yalnızca TOPLAMI
    // değiştirir, fark aylara net tutarları oranında dağıtılır ve kuruş farkı son aya yazılır.
    // Sonuç her ay için yine tam bir snapshot'tır (taban + yüzde + gerekçe + net) - aidat
    // satırına bakan kişi "bu tutar nereden geldi"yi hâlâ satırın kendisinden okur.
    //
    // Sınırlar: toplam 0'dan büyük ve tarife (indirimsiz) toplamından büyük olamaz - aidat
    // tarifenin üstüne çıkamaz (CK_receivables_discount_percent 0..100).
    public const string ManualAdjustmentReason = "Tahsilatta elle düzeltme";

    public static IReadOnlyList<Breakdown> AdjustToAgreedTotal(IReadOnlyList<Breakdown> months, decimal agreedTotal)
    {
        if (months.Count == 0) throw new ArgumentException("En az bir ay gerekli.", nameof(months));

        var agreed = Round(agreedTotal);
        var computed = months.Sum(month => month.NetAmount);
        var baseTotal = months.Sum(month => month.BaseAmount);
        if (agreed <= 0) throw new ArgumentException("Tahsil edilen tutar 0'dan büyük olmalı.", nameof(agreedTotal));
        if (agreed > baseTotal)
            throw new ArgumentException("Tahsil edilen tutar tarife toplamını aşamaz.", nameof(agreedTotal));
        if (agreed == computed) return months;

        var result = new List<Breakdown>(months.Count);
        var allocated = 0m;
        for (var index = 0; index < months.Count; index++)
        {
            var month = months[index];
            var isLast = index == months.Count - 1;
            // Hesaplanan toplam 0 ise (tamamen indirimli aylar) tabana göre dağıtılır.
            var share = computed > 0 ? month.NetAmount / computed : month.BaseAmount / baseTotal;
            var net = isLast ? agreed - allocated : Round(agreed * share);
            // Tek bir ay tabanını aşamaz; aşan kısım sonraki aylara kayar (son ay en sonda
            // kalanın tamamını alır - toplam agreed ≤ baseTotal olduğu için sığar).
            net = Math.Clamp(net, 0m, month.BaseAmount);
            allocated += net;
            result.Add(WithNet(month, net));
        }

        // Kıstırma son ayda kuruş artığı bırakabilir; tabanı dolmamış aylara geri dağıt.
        var leftover = agreed - allocated;
        for (var index = result.Count - 1; leftover > 0 && index >= 0; index--)
        {
            var room = result[index].BaseAmount - result[index].NetAmount;
            if (room <= 0) continue;
            var add = Math.Min(room, leftover);
            result[index] = WithNet(result[index], result[index].NetAmount + add);
            leftover -= add;
        }

        return result;
    }

    private static Breakdown WithNet(Breakdown month, decimal net)
    {
        var percent = month.BaseAmount > 0 ? Round(100m - net / month.BaseAmount * 100m) : 0m;
        percent = Math.Clamp(percent, 0m, 100m);
        var reason = month.DiscountReason is null ? ManualAdjustmentReason : $"{month.DiscountReason} + {ManualAdjustmentReason}";
        if (reason.Length > 200) reason = reason[..200];
        return new Breakdown(month.BaseAmount, percent, reason, Round(net));
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
