using Abdera.Api.Modules.Library.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Library.Persistence;

public class ScoreFileConfiguration : IEntityTypeConfiguration<ScoreFile>
{
    public void Configure(EntityTypeBuilder<ScoreFile> builder)
    {
        builder.ToTable("library_score_files", table =>
        {
            table.HasCheckConstraint("ck_library_score_files_size", "size_bytes > 0");
            table.HasCheckConstraint("ck_library_score_files_pages", "page_count IS NULL OR page_count > 0");
        });
        builder.HasKey(f => f.EntryId);
        builder.Property(f => f.EntryId).HasColumnName("entry_id").HasMaxLength(100);
        builder.Property(f => f.Content).HasColumnName("content");
        builder.Property(f => f.SizeBytes).HasColumnName("size_bytes");
        builder.Property(f => f.PageCount).HasColumnName("page_count");
        builder.Property(f => f.Version).HasColumnName("version");
        builder.Property(f => f.UploadedBy).HasColumnName("uploaded_by");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at");
    }
}
