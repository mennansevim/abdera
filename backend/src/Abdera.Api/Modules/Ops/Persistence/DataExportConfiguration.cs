using Abdera.Api.Modules.Ops.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Ops.Persistence;

public class DataExportConfiguration : IEntityTypeConfiguration<DataExport>
{
    public void Configure(EntityTypeBuilder<DataExport> builder)
    {
        builder.ToTable("data_exports");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.RequestedBy).HasColumnName("requested_by");
        builder.Property(e => e.FileName).HasColumnName("file_name").HasMaxLength(100);
        builder.Property(e => e.Content).HasColumnName("content");
        builder.Property(e => e.SizeBytes).HasColumnName("size_bytes");
        builder.Property(e => e.ErrorMessage).HasColumnName("error_message").HasMaxLength(DataExport.MaxErrorLength);
        builder.Property(e => e.CompletedAt).HasColumnName("completed_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(e => e.CreatedAt);
    }
}
