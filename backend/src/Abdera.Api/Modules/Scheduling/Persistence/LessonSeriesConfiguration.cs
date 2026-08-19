using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Scheduling.Persistence;

public class LessonSeriesConfiguration : IEntityTypeConfiguration<LessonSeries>
{
    public void Configure(EntityTypeBuilder<LessonSeries> builder)
    {
        builder.ToTable("lesson_series");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.EnrollmentId).HasColumnName("enrollment_id");
        builder.Property(s => s.DayOfWeek).HasColumnName("day_of_week").HasConversion<int>();
        builder.Property(s => s.StartTime).HasColumnName("start_time");
        builder.Property(s => s.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(s => s.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(s => s.EffectiveUntil).HasColumnName("effective_until");
        builder.Property(s => s.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(s => s.EnrollmentId);
    }
}
