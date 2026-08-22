using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Features;
using Abdera.Api.Modules.Ops.Infrastructure;

namespace Abdera.Api.Modules.Ops;

// Sağlık kontrolü, yedekleme ve e-posta alarmı - Faz 4 (docs/15-product-phases.md).
// Sağlayıcı seçimi (Backup__Provider, Email__Provider) Program.cs'te - WhatsApp/Banking'teki
// gibi yapısal bir DI kararı, Build()'den önce verilmek zorunda (bkz. CLAUDE.md notu).
public static class OpsModule
{
    public static void AddOpsModule(this IServiceCollection services)
    {
        // BackupService hem bir BackgroundService hem de manuel tetikleme uç noktasının
        // enjekte ettiği bir singleton - ikisinin AYNI örneği paylaşması gerekiyor
        // (LastRunDate/_runLock durumu), bu yüzden AddSingleton + AddHostedService(sp=>...) .
        services.AddSingleton<BackupService>();
        services.AddHostedService(sp => sp.GetRequiredService<BackupService>());
        services.AddHostedService<SystemHealthMonitor>();
    }

    public static void MapOpsModule(this WebApplication app)
    {
        app.MapBackupRuns();
        app.MapSystemHealth();
    }
}
