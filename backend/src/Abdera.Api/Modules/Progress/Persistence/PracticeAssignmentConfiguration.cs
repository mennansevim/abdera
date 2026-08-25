using Abdera.Api.Modules.Progress.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Progress.Persistence;

public class PracticeAssignmentConfiguration : IEntityTypeConfiguration<PracticeAssignment>
{
    public void Configure(EntityTypeBuilder<PracticeAssignment> builder)
    {
        builder.ToTable("practice_assignments");
        builder.HasKey(assignment => assignment.Id);
        builder.Property(assignment => assignment.Id).HasColumnName("id");
        builder.Property(assignment => assignment.LessonId).HasColumnName("lesson_id");
        builder.Property(assignment => assignment.Description).HasColumnName("description").HasMaxLength(2000).IsRequired();
        builder.Property(assignment => assignment.DueDate).HasColumnName("due_date");
        builder.Property(assignment => assignment.Completed).HasColumnName("completed").HasDefaultValue(false);
        builder.Property(assignment => assignment.CreatedAt).HasColumnName("created_at");
        builder.Property(assignment => assignment.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(assignment => assignment.LessonId);
        builder.HasOne<Abdera.Api.Modules.Scheduling.Domain.Lesson>().WithMany()
            .HasForeignKey(assignment => assignment.LessonId).OnDelete(DeleteBehavior.Restrict);
    }
}
