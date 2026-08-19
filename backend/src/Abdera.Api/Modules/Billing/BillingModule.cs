using Abdera.Api.Modules.Billing.Features;

namespace Abdera.Api.Modules.Billing;

public static class BillingModule
{
    public static void MapBillingModule(this WebApplication app)
    {
        app.MapMakeupCredits();
    }
}
