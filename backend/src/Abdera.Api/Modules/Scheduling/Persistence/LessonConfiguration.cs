using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Scheduling.Persistence;

public class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.ToTable("lessons");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");
        builder.Property(l => l.LessonSeriesId).HasColumnName("lesson_series_id");
        builder.Property(l => l.StudentId).HasColumnName("student_id");
        builder.Property(l => l.TeacherId).HasColumnName("teacher_id");
        builder.Property(l => l.InstrumentId).HasColumnName("instrument_id");
        builder.Property(l => l.StartAt).HasColumnName("start_at");
        builder.Property(l => l.EndAt).HasColumnName("end_at");
        builder.Property(l => l.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.OriginalLessonId).HasColumnName("original_lesson_id");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at");

        // docs/03-erd.md kritik kısıt - aynı seri aynı saatte iki kez üretilemez.
        builder.HasIndex(l => new { l.LessonSeriesId, l.StartAt }).IsUnique();
        builder.HasIndex(l => l.TeacherId);
        builder.HasIndex(l => l.StudentId);
        builder.HasIndex(l => l.StartAt);

        builder.ToTable(t => t.HasCheckConstraint("CK_lessons_time_range", "end_at > start_at"));
    }
}
