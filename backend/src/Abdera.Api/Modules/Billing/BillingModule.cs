using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.Billing.Infrastructure;

namespace Abdera.Api.Modules.Billing;

public static class BillingModule
{
    public static void AddBillingModule(this IServiceCollection services)
    {
        services.AddHostedService<OverdueReceivableSweeper>();
    }

    public static void MapBillingModule(this WebApplication app)
    {
        app.MapMakeupCredits();
        app.MapFeePlans();
        app.MapReceivables();
        app.MapBulkReceivables();
        app.MapPayments();
        app.MapPaymentCorrections();
        app.MapBulkPayments();
        app.MapExpenses();
        app.MapStudentBilling();
        app.MapSendPaymentReminder();
    }
}
