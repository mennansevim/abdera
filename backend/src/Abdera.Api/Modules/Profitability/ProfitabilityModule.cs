using Abdera.Api.Modules.Profitability.Features;

namespace Abdera.Api.Modules.Profitability;

// Kârlılık sekmesi (docs/10-decisions.md V): okulun güncel verisinden net kâr, birim ekonomisi,
// işleyiş eksikleri, yöneticinin fikirleri ve aylık yapay zekâ yorumu.
public static class ProfitabilityModule
{
    public static void MapProfitabilityModule(this WebApplication app)
    {
        app.MapProfitability();
        app.MapProfitCommentaries();
    }
}
