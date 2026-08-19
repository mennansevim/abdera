using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Shared;

// Database:AutoMigrate (varsayılan true) - küçük ölçekli bu sistemde `docker compose up`
// tek başına çalışan bir kurulum vermeli (docs/08-migrations.md). Operatör canlıda
// migration zamanlamasını elle yönetmek isterse Database__AutoMigrate=false yapıp
// SDK içeren bir imajdan `dotnet ef database update` çalıştırabilir - runtime imajı
// (aspnet:10.0-alpine) yalnızca çalışma zamanını içerir, dotnet-ef aracını içermez.
public static class DatabaseMigrator
{
    public static async Task RunAsync(WebApplication app)
    {
        if (!app.Configuration.GetValue("Database:AutoMigrate", true))
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Veritabanı şeması güncel, bekleyen migration yok.");
            return;
        }

        logger.LogInformation("Uygulanacak migration'lar: {Migrations}", string.Join(", ", pending));
        await db.Database.MigrateAsync();
        logger.LogInformation("Migration'lar uygulandı.");
    }
}
