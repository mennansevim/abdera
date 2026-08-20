using Abdera.Api.Modules.Pricing.Features;

namespace Abdera.Api.Modules.Pricing;

public static class PricingModule
{
    public static void MapPricingModule(this WebApplication app)
    {
        app.MapPriceLists();
        app.MapBulkUpdate();
    }
}
