using Abdera.Api.Modules.Progress.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Progress.Persistence;

public class LessonNoteConfiguration : IEntityTypeConfiguration<LessonNote>
{
    public void Configure(EntityTypeBuilder<LessonNote> builder)
    {
        builder.ToTable("lesson_notes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id");
        builder.Property(n => n.LessonId).HasColumnName("lesson_id");
        builder.Property(n => n.TeacherId).HasColumnName("teacher_id");
        builder.Property(n => n.Practiced).HasColumnName("practiced");
        builder.Property(n => n.Note).HasColumnName("note");
        builder.Property(n => n.Homework).HasColumnName("homework");
        builder.Property(n => n.NextGoal).HasColumnName("next_goal");
        builder.Property(n => n.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(n => n.LessonId);
    }
}
