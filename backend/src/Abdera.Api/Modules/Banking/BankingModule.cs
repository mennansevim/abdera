using Abdera.Api.Modules.Banking.Features;

namespace Abdera.Api.Modules.Banking;

public static class BankingModule
{
    public static void MapBankingModule(this WebApplication app)
    {
        app.MapAssignVirtualIban();
        app.MapBankTransactions();
        app.MapWebhooks();

        if (app.Environment.IsDevelopment())
        {
            app.MapDevBankSimulator();
        }
    }
}
