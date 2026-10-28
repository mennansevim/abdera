using System.Globalization;
using Abdera.Platform.Api.Domain;

namespace Abdera.Platform.Api.Features;

// Liste fiyatı (Pricing ayarı): sihirbazın ve yeni açılan eklentinin başlangıç değeri. Okula yazılan
// fiyat bundan kopyalanır ve donar (SchoolPackage); liste fiyatını değiştirmek kurulu okulları etkilemez.
// Bütün tutarlar yıllıktır (lisans yıllık).
//   Pricing__PricePerSeat=450
//   Pricing__Addons__whatsapp=300
public static class Pricing
{
    public record PricingResponse(string Currency, decimal PricePerSeat, IReadOnlyDictionary<string, decimal> AddonPrices);

    public static void MapPricing(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/pricing", (IConfiguration config) => Results.Ok(Defaults(config))).RequireAuthorization(Auth.OperatorPolicy);
    }

    // İstek anında okunur (WebApplicationFactory'nin test ayarları da görülsün).
    public static PricingResponse Defaults(IConfiguration config)
    {
        var addons = config.GetSection("Pricing:Addons").GetChildren()
            .Select(child => (child.Key, Price: Money(child.Value)))
            .Where(item => item.Price is not null)
            .ToDictionary(item => item.Key.ToLowerInvariant(), item => item.Price!.Value);
        return new PricingResponse(
            config["Pricing:Currency"] ?? SchoolPackage.DefaultCurrency,
            Money(config["Pricing:PricePerSeat"]) ?? 0m,
            addons);
    }

    private static decimal? Money(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) && amount >= 0 ? decimal.Round(amount, 2) : null;
}
