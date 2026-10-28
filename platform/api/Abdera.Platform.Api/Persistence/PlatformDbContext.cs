using System.Text;
using Abdera.Platform.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Api.Persistence;

// Platformun kendi veritabanı (okul veritabanlarından tamamen ayrı). Kolon adları snake_case;
// okul uygulamasıyla aynı kurallar: zaman timestamptz, enum metin, xmin ile iyimser eşzamanlılık.
public class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public DbSet<School> Schools => Set<School>();
    public DbSet<ServerHost> Hosts => Set<ServerHost>();
    public DbSet<SchoolSecret> SchoolSecrets => Set<SchoolSecret>();
    public DbSet<ProvisioningRun> Runs => Set<ProvisioningRun>();
    public DbSet<ProvisioningStep> Steps => Set<ProvisioningStep>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<Rollout> Rollouts => Set<Rollout>();
    public DbSet<TelemetrySnapshot> Telemetry => Set<TelemetrySnapshot>();
    public DbSet<PlatformAuditLog> AuditLogs => Set<PlatformAuditLog>();
    public DbSet<SchoolAlert> Alerts => Set<SchoolAlert>();
    public DbSet<PlatformUser> Users => Set<PlatformUser>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<School>(e =>
        {
            e.ToTable("schools", t =>
            {
                t.HasCheckConstraint("CK_schools_slug", "slug ~ '^[a-z][a-z0-9-]{2,30}$'");
                t.HasCheckConstraint("CK_schools_staff_seat_limit", $"staff_seat_limit IS NULL OR staff_seat_limit BETWEEN 1 AND {SchoolPackage.MaxSeats}");
                t.HasCheckConstraint("CK_schools_price_per_seat", "price_per_seat IS NULL OR price_per_seat >= 0");
                t.HasCheckConstraint("CK_schools_license_period",
                    "(license_starts_on IS NULL AND license_ends_on IS NULL) OR license_ends_on >= license_starts_on");
            });
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(31);
            e.Property(x => x.DisplayName).HasMaxLength(120);
            e.Property(x => x.ShortName).HasMaxLength(40);
            e.Property(x => x.PrimaryDomain).HasMaxLength(253);
            e.HasIndex(x => x.PrimaryDomain).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PrimaryColor).HasMaxLength(7);
            e.Property(x => x.Modules).HasColumnType("text[]");
            e.Property(x => x.Categories).HasColumnType("text[]").HasDefaultValueSql("'{}'::text[]");
            e.Property(x => x.CustomCategoryNamesJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            e.Ignore(x => x.CustomCategoryNames);
            e.Ignore(x => x.PublicDomain);
            e.Property(x => x.CustomDomain).HasMaxLength(253);
            e.Property(x => x.CustomDomainCheck).HasMaxLength(300);
            e.HasIndex(x => x.CustomDomain).IsUnique().HasFilter("custom_domain IS NOT NULL");
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Ignore(x => x.ModulesEnvValue);
            e.Property(x => x.PricePerSeat).HasColumnType("numeric(12,2)");
            e.Property(x => x.Currency).HasMaxLength(3).HasDefaultValue(SchoolPackage.DefaultCurrency);
            e.Property(x => x.AddonPricesJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            e.Ignore(x => x.AddonPrices);
            e.Ignore(x => x.YearlyTotal);
        });

        model.Entity<ServerHost>(e =>
        {
            e.ToTable("hosts", t => t.HasCheckConstraint("CK_hosts_reserved", "reserved_memory_mb >= 0"));
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Ignore(x => x.CapacityMb);
        });

        model.Entity<SchoolSecret>(e =>
        {
            e.ToTable("school_secrets");
            e.HasKey(x => new { x.SchoolId, x.Name });
            e.Property(x => x.Name).HasMaxLength(120);
        });

        model.Entity<ProvisioningRun>(e =>
        {
            e.ToTable("provisioning_runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Ignore(x => x.IsOpen);
            // Bir okulda aynı anda tek açık işlem (kurulum, güncelleme, askı...): iki işlem aynı
            // okulun dosyalarını/yönlendirmesini yarıştırmasın.
            e.HasIndex(x => x.SchoolId).IsUnique().HasFilter("status IN ('Pending', 'Running')")
                .HasDatabaseName("UX_provisioning_runs_one_open_per_school");
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });

        model.Entity<ProvisioningStep>(e =>
        {
            e.ToTable("provisioning_steps");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(60);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.RunId, x.Step, x.Attempt }).IsUnique();
        });

        model.Entity<Release>(e =>
        {
            e.ToTable("releases");
            e.HasKey(x => x.Version);
            e.Property(x => x.Version).HasMaxLength(20);
            e.Property(x => x.ModuleCatalogJson).HasColumnType("jsonb");
            e.Property(x => x.CategoryCatalogJson).HasColumnType("jsonb").HasDefaultValue("[]");
        });

        model.Entity<Rollout>(e =>
        {
            e.ToTable("rollouts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RowVersion).IsRowVersion();
            // Aynı anda tek bir filo dağıtımı.
            e.HasIndex(x => x.Status).IsUnique().HasFilter("status IN ('Running', 'Paused')")
                .HasDatabaseName("UX_rollouts_one_active");
        });

        model.Entity<TelemetrySnapshot>(e =>
        {
            e.ToTable("telemetry_snapshots");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SchoolId, x.TakenAt });
        });

        model.Entity<PlatformAuditLog>(e =>
        {
            e.ToTable("platform_audit_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.BeforeJson).HasColumnType("jsonb");
            e.Property(x => x.AfterJson).HasColumnType("jsonb");
            e.HasIndex(x => x.CreatedAt);
        });

        model.Entity<SchoolAlert>(e =>
        {
            e.ToTable("school_alerts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SchoolId, x.ReceivedAt });
        });

        model.Entity<PlatformUser>(e =>
        {
            e.ToTable("platform_users");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
        });

        // Tüm kolonlar snake_case (okul veritabanıyla aynı görünüm; ek paket yok).
        // RowVersion atlanır: Npgsql onu Postgres'in sistem kolonu xmin'e eşler (iyimser eşzamanlılık).
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                if (property.Name != "RowVersion")
                    property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }
}
