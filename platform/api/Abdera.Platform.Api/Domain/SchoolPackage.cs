namespace Abdera.Platform.Api.Domain;

// Okulun satın aldığı paket (ARCHITECTURE.md §5.7): fiyatın tek ekseni personel hesabı sayısıdır
// (yönetici + öğretmen; veli ve öğrenci sayılmaz), ücretli eklentiler bunun üstüne sabit aylık tutar
// ekler. Paketin tamamı okul satırına donar: liste fiyatı (Pricing ayarı) sonradan değişse de var olan
// okulun fiyatı değişmez. Aylık tutarın hesaplandığı tek yer burası - panel yalnızca gösterir.
public static class SchoolPackage
{
    public const int MaxSeats = 1000;
    public const decimal MaxPrice = 1_000_000m;
    public const string DefaultCurrency = "TRY";

    public static decimal MonthlyTotal(
        int? staffSeats, decimal? pricePerSeat, IReadOnlyDictionary<string, decimal> addonPrices, IEnumerable<string> modules)
    {
        var seats = staffSeats is { } count && pricePerSeat is { } price ? count * price : 0m;
        var addons = modules.Distinct().Sum(module => addonPrices.GetValueOrDefault(module));
        return decimal.Round(seats + addons, 2, MidpointRounding.AwayFromZero);
    }

    public static void Validate(int? staffSeats, decimal? pricePerSeat, IReadOnlyDictionary<string, decimal> addonPrices, IReadOnlyCollection<string> modules)
    {
        if (staffSeats is { } seats && (seats < 1 || seats > MaxSeats))
            throw ValidationFailedException.For("package.staffSeats", $"Kullanıcı sayısı 1 ile {MaxSeats} arasında olmalı.");
        if (staffSeats is not null && pricePerSeat is null)
            throw ValidationFailedException.For("package.pricePerSeat", "Kullanıcı başı fiyat girilmeli.");
        if (pricePerSeat is { } price && !IsMoney(price))
            throw ValidationFailedException.For("package.pricePerSeat", "Fiyat 0 ya da daha büyük, en fazla iki ondalık olmalı.");
        foreach (var (module, addonPrice) in addonPrices)
        {
            if (!modules.Contains(module))
                throw ValidationFailedException.For("package.addonPrices", $"Seçilmemiş eklentiye fiyat verilmiş: {module}");
            if (!IsMoney(addonPrice))
                throw ValidationFailedException.For("package.addonPrices", "Eklenti fiyatı 0 ya da daha büyük, en fazla iki ondalık olmalı.");
        }
    }

    private static bool IsMoney(decimal value) => value >= 0 && value <= MaxPrice && decimal.Round(value, 2) == value;
}
