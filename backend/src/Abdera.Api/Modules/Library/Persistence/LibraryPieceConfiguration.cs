using Abdera.Api.Modules.Library.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Library.Persistence;

public class LibraryPieceConfiguration : IEntityTypeConfiguration<LibraryPiece>
{
    public void Configure(EntityTypeBuilder<LibraryPiece> builder)
    {
        builder.ToTable("library_pieces", table =>
            table.HasCheckConstraint("ck_library_pieces_level", "level IS NULL OR level BETWEEN 1 AND 5"));
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.Title).HasColumnName("title").HasMaxLength(200);
        builder.Property(p => p.Composer).HasColumnName("composer").HasMaxLength(200);
        builder.Property(p => p.Instrument).HasColumnName("instrument").HasMaxLength(30);
        builder.Property(p => p.Category).HasColumnName("category").HasMaxLength(30);
        builder.Property(p => p.Level).HasColumnName("level");
        builder.Property(p => p.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(p => p.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        builder.Ignore(p => p.EntryId);
    }
}
