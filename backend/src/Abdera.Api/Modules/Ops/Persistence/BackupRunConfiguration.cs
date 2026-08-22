using Abdera.Api.Modules.Ops.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Ops.Persistence;

public class BackupRunConfiguration : IEntityTypeConfiguration<BackupRun>
{
    public void Configure(EntityTypeBuilder<BackupRun> builder)
    {
        builder.ToTable("backup_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.TriggeredManually).HasColumnName("triggered_manually");
        builder.Property(r => r.StartedAt).HasColumnName("started_at");
        builder.Property(r => r.CompletedAt).HasColumnName("completed_at");
        builder.Property(r => r.SizeBytes).HasColumnName("size_bytes");
        builder.Property(r => r.RemotePath).HasColumnName("remote_path").HasMaxLength(300);
        builder.Property(r => r.ErrorMessage).HasColumnName("error_message");

        builder.HasIndex(r => r.StartedAt);
    }
}
