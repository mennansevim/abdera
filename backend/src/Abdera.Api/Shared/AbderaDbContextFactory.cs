using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Abdera.Api.Shared;

// Migration üretimi uygulamanın production guard/worker/migration başlangıç yolunu
// çalıştırmamalı. Bağlantı yalnız model üretimi için gereklidir; gerçek update komutu
// kendi ConnectionStrings__Default değerini kullanır.
public class AbderaDbContextFactory : IDesignTimeDbContextFactory<AbderaDbContext>
{
    public AbderaDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AbderaDbContext>()
            .UseNpgsql("Host=localhost;Database=abdera_design;Username=abdera;Password=design-time-only")
            .Options;
        return new AbderaDbContext(options);
    }
}
