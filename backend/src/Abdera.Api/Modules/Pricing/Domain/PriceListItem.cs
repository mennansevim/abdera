namespace Abdera.Api.Modules.Pricing.Domain;

// docs/03-erd.md - Pricing > price_list_items. Enstrüman x ders süresi x ücretlendirme
// tipi kombinasyonu için birim fiyat. Kendi tarih aralığı yok - üst PriceList'inkini miras
// alır (aynı kombinasyon için çakışan aralık kontrolü PriceLists.cs'de yapılır).
public class PriceListItem
{
    public Guid Id { get; private set; }
    public Guid PriceListId { get; private set; }
    public Guid InstrumentId { get; private set; }
    public int DurationMinutes { get; private set; }
    public BillingType BillingType { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public int? PackageLessonCount { get; private set; }

    private PriceListItem() { }

    public static PriceListItem Create(
        Guid priceListId, Guid instrumentId, int durationMinutes, BillingType billingType,
        decimal amount, string currency, int? packageLessonCount)
    {
        if (amount < 0) throw new ArgumentException("Tutar negatif olamaz.", nameof(amount));
        if (durationMinutes <= 0) throw new ArgumentException("Süre pozitif olmalı.", nameof(durationMinutes));
        if (billingType == BillingType.Package && packageLessonCount is null or <= 0)
            throw new ArgumentException("Paket tipi için ders sayısı pozitif olmalı.", nameof(packageLessonCount));
        if (billingType == BillingType.Monthly && packageLessonCount is not null)
            throw new ArgumentException("Aylık tip için paket ders sayısı verilmemeli.", nameof(packageLessonCount));

        return new PriceListItem
        {
            Id = Guid.NewGuid(),
            PriceListId = priceListId,
            InstrumentId = instrumentId,
            DurationMinutes = durationMinutes,
            BillingType = billingType,
            Amount = amount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "TRY" : currency.Trim().ToUpperInvariant(),
            PackageLessonCount = packageLessonCount,
        };
    }

    // docs/10-decisions.md A1: toplu zam bu listenin kalemlerini değiştirir - geçmiş
    // Receivable'lar zaten kendi tutarını kopyaladığı için etkilenmez.
    public void ApplyPercentageChange(decimal percentage)
    {
        var newAmount = Math.Round(Amount * (1 + percentage / 100m), 2, MidpointRounding.AwayFromZero);
        if (newAmount < 0) throw new ArgumentException("Yüzde değişimi negatif tutara yol açamaz.", nameof(percentage));
        Amount = newAmount;
    }
}
