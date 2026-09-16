namespace Abdera.Api.Modules.Billing.Domain;

// Yıl başı toplu ödeme kampanyasının kademeleri ("Toplu ödemelerde indirim uygulanacaktır").
// Örn. 4 ay -> %5, 10 ay -> %10. Verilen ay sayısı için MinMonths'u aşmayan EN YÜKSEK
// kademe uygulanır (bkz. BillingPolicy.ResolvePrepayPercent).
public class PrepayDiscountTier
{
    public Guid Id { get; private set; }
    public int MinMonths { get; private set; }
    public decimal Percent { get; private set; }

    private PrepayDiscountTier() { }

    public static PrepayDiscountTier Create(int minMonths, decimal percent)
    {
        if (minMonths is < 2 or > 24)
            throw new ArgumentOutOfRangeException(nameof(minMonths), "Kademe 2 ile 24 ay arasında olmalı.");
        if (percent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percent), "İndirim yüzdesi 0 ile 100 arasında olmalı.");

        return new PrepayDiscountTier { Id = Guid.NewGuid(), MinMonths = minMonths, Percent = percent };
    }
}
