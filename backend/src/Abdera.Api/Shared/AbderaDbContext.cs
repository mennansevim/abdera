using Abdera.Api.Modules.Auth.Domain;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Shared;

// CLAUDE.md: tek AbderaDbContext, modül başına ayrı context yok. Repository pattern yok -
// handler'lar bu context'i doğrudan kullanır. Yeni bir modül eklendikçe buraya DbSet eklenir
// ve ApplyConfigurationsFromAssembly ilgili Persistence/*Configuration.cs dosyasını otomatik bulur.
public class AbderaDbContext : DbContext
{
    public AbderaDbContext(DbContextOptions<AbderaDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AbderaDbContext).Assembly);
    }
}
