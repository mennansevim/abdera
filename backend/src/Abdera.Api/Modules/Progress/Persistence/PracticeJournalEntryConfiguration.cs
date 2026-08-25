using Abdera.Api.Modules.Progress.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Progress.Persistence;

public class PracticeJournalEntryConfiguration : IEntityTypeConfiguration<PracticeJournalEntry>
{
    public void Configure(EntityTypeBuilder<PracticeJournalEntry> builder)
    {
        builder.ToTable("practice_journal_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).HasColumnName("id");
        builder.Property(entry => entry.StudentId).HasColumnName("student_id");
        builder.Property(entry => entry.PracticeDate).HasColumnName("practice_date");
        builder.Property(entry => entry.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(entry => entry.Goal).HasColumnName("goal").HasMaxLength(500).IsRequired();
        builder.Property(entry => entry.Note).HasColumnName("note").HasMaxLength(2000);
        builder.Property(entry => entry.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(entry => entry.ParentApprovedAt).HasColumnName("parent_approved_at");
        builder.Property(entry => entry.ParentApprovedByGuardianId).HasColumnName("parent_approved_by_guardian_id");
        builder.Property(entry => entry.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(entry => new { entry.StudentId, entry.PracticeDate });
        builder.HasOne<Abdera.Api.Modules.People.Domain.Student>().WithMany()
            .HasForeignKey(entry => entry.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Abdera.Api.Modules.People.Domain.Guardian>().WithMany()
            .HasForeignKey(entry => entry.ParentApprovedByGuardianId).OnDelete(DeleteBehavior.Restrict);
    }
}
