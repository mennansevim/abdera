using Abdera.Api.Modules.Assistant.Features;

namespace Abdera.Api.Modules.Assistant;

// Uygulama içi asistan (docs/10-decisions.md AE). İlk sürüm yalnızca kural yolu: niyeti
// çözer, veri değiştirmez; tablo, servis veya arka plan işi yoktur.
public static class AssistantModule
{
    public static void MapAssistantModule(this WebApplication app)
    {
        app.MapResolveIntent();
    }
}
