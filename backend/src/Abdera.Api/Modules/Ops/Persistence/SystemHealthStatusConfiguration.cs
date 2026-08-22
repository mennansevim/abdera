using Abdera.Api.Modules.Ops.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Ops.Persistence;

public class SystemHealthStatusConfiguration : IEntityTypeConfiguration<SystemHealthStatus>
{
    public void Configure(EntityTypeBuilder<SystemHealthStatus> builder)
    {
        builder.ToTable("system_health_status");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.Level).HasColumnName("level").HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Detail).HasColumnName("detail");
        builder.Property(s => s.LastCheckedAt).HasColumnName("last_checked_at");
        builder.Property(s => s.LastAlertSentAt).HasColumnName("last_alert_sent_at");
    }
}
