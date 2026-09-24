using Abdera.Api.Modules.Library.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Library.Persistence;

public class LibrarySuggestionConfiguration : IEntityTypeConfiguration<LibrarySuggestion>
{
    public void Configure(EntityTypeBuilder<LibrarySuggestion> builder)
    {
        builder.ToTable("library_suggestions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.StudentId).HasColumnName("student_id");
        builder.Property(s => s.EntryId).HasColumnName("entry_id").HasMaxLength(100);
        builder.Property(s => s.Title).HasColumnName("title").HasMaxLength(200);
        builder.Property(s => s.Composer).HasColumnName("composer").HasMaxLength(200);
        builder.Property(s => s.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(s => s.TeacherId).HasColumnName("teacher_id");
        builder.Property(s => s.SuggestedByName).HasColumnName("suggested_by_name").HasMaxLength(200);
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        // Aynı eser bir öğrenciye iki kez önerilmesin (iki öğretmen aynı anda önerse de).
        builder.HasIndex(s => new { s.StudentId, s.EntryId }).IsUnique();
    }
}
