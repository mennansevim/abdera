using Abdera.Api.Modules.Ops.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Ops.Persistence;

public class JobHeartbeatConfiguration : IEntityTypeConfiguration<JobHeartbeat>
{
    public void Configure(EntityTypeBuilder<JobHeartbeat> builder)
    {
        builder.ToTable("job_heartbeats");
        builder.HasKey(h => h.JobName);
        builder.Property(h => h.JobName).HasColumnName("job_name").HasMaxLength(64);
        builder.Property(h => h.LastSucceededAt).HasColumnName("last_succeeded_at");
        builder.Property(h => h.LastFailedAt).HasColumnName("last_failed_at");
        builder.Property(h => h.LastError).HasColumnName("last_error").HasMaxLength(JobHeartbeat.MaxErrorLength);
        builder.Property(h => h.CreatedAt).HasColumnName("created_at");
        builder.Property(h => h.UpdatedAt).HasColumnName("updated_at");
    }
}
