using Abdera.Api.Modules.Dashboard.Features;

namespace Abdera.Api.Modules.Dashboard;

// docs/02-modules.md: Dashboard salt-okunur bir sorgu modeli, kendi tablosu/servisi yok -
// bu yüzden DI kaydı gerektiren bir AddDashboardModule yok, yalnızca endpoint kaydı var.
public static class DashboardModule
{
    public static void MapDashboardModule(this WebApplication app)
    {
        app.MapDashboard();
    }
}
