using Abdera.Api.Modules.Profitability.Features;
using Abdera.Api.Shared.Modules;

namespace Abdera.Api.Modules.Profitability;

// Kârlılık sekmesi (docs/10-decisions.md V): okulun güncel verisinden net kâr, birim ekonomisi,
// işleyiş eksikleri, yöneticinin fikirleri ve aylık yapay zekâ yorumu.
public static class ProfitabilityModule
{
    public static void MapProfitabilityModule(this WebApplication app)
    {
        var module = app.MapModule(ModuleKeys.Profitability);
        module.MapProfitability();
        module.MapProfitCommentaries();
    }
}
