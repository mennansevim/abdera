using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.Billing.Infrastructure;

namespace Abdera.Api.Modules.Billing;

public static class BillingModule
{
    public static void AddBillingModule(this IServiceCollection services, bool enableHostedServices = true)
    {
        if (enableHostedServices)
        {
            services.AddHostedService<OverdueReceivableSweeper>();
            services.AddHostedService<MonthlyReceivableGenerator>();
        }
    }

    public static void MapBillingModule(this WebApplication app)
    {
        app.MapMakeupCredits();
        app.MapTuitionRates();
        app.MapBillingPolicy();
        app.MapReceivables();
        app.MapMonthlyDueRun();
        app.MapPayments();
        app.MapPaymentCorrections();
        app.MapPrepayPlans();
        app.MapExpenses();
        app.MapStudentBilling();
        app.MapSendPaymentReminder();
    }
}
