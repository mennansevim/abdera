using Abdera.Api.Modules.Progress.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Progress.Persistence;

public class ProgressSummaryConfiguration : IEntityTypeConfiguration<ProgressSummary>
{
    public void Configure(EntityTypeBuilder<ProgressSummary> builder)
    {
        builder.ToTable("progress_summaries");
        builder.HasKey(summary => summary.Id);
        builder.Property(summary => summary.Id).HasColumnName("id");
        builder.Property(summary => summary.StudentId).HasColumnName("student_id");
        builder.Property(summary => summary.TeacherId).HasColumnName("teacher_id");
        builder.Property(summary => summary.Summary).HasColumnName("summary").HasMaxLength(2000).IsRequired();
        builder.Property(summary => summary.SourceNoteCount).HasColumnName("source_note_count");
        builder.Property(summary => summary.SourceLatestNoteAt).HasColumnName("source_latest_note_at");
        builder.Property(summary => summary.Model).HasColumnName("model").HasMaxLength(100).IsRequired();
        builder.Property(summary => summary.CreatedAt).HasColumnName("created_at");
        builder.Property(summary => summary.UpdatedAt).HasColumnName("updated_at");

        // Öğrenci + kapsam başına tek önbellek satırı. NULLS NOT DISTINCT: yönetici görünümü
        // (teacher_id NULL) de tekil olmalı - iki eşzamanlı ilk açılış iki satır yazamasın.
        builder.HasIndex(summary => new { summary.StudentId, summary.TeacherId })
            .IsUnique()
            .AreNullsDistinct(false);
        builder.ToTable(table => table.HasCheckConstraint("ck_progress_summaries_source_note_count", "source_note_count > 0"));

        builder.HasOne<Abdera.Api.Modules.People.Domain.Student>().WithMany()
            .HasForeignKey(summary => summary.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Abdera.Api.Modules.People.Domain.Teacher>().WithMany()
            .HasForeignKey(summary => summary.TeacherId).OnDelete(DeleteBehavior.Restrict);
    }
}
