using Abdera.Api.Modules.Banking.Features;
using Abdera.Api.Shared.Modules;

namespace Abdera.Api.Modules.Banking;

public static class BankingModule
{
    public static void MapBankingModule(this WebApplication app)
    {
        var module = app.MapModule(ModuleKeys.Banking);
        module.MapAssignVirtualIban();
        module.MapBankTransactions();
        module.MapWebhooks();

        if (app.Environment.IsDevelopment())
        {
            module.MapDevBankSimulator();
        }
    }
}
