using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Abdera.Platform.Api.Persistence;

// Migration üretimi uygulamanın başlangıç yolunu (işçiler, bootstrap) çalıştırmasın; bağlantı yalnızca
// model üretimi içindir (okul uygulamasındaki AbderaDbContextFactory ile aynı).
public class PlatformDbContextFactory : IDesignTimeDbContextFactory<PlatformDbContext>
{
    public PlatformDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql("Host=localhost;Database=abdera_platform_design;Username=abdera;Password=design-time-only")
            .Options);
}
