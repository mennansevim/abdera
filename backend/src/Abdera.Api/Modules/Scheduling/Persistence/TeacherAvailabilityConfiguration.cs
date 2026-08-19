using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Scheduling.Persistence;

public class TeacherAvailabilityConfiguration : IEntityTypeConfiguration<TeacherAvailability>
{
    public void Configure(EntityTypeBuilder<TeacherAvailability> builder)
    {
        builder.ToTable("teacher_availability");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.TeacherId).HasColumnName("teacher_id");
        builder.Property(a => a.DayOfWeek).HasColumnName("day_of_week").HasConversion<int>();
        builder.Property(a => a.StartTime).HasColumnName("start_time");
        builder.Property(a => a.EndTime).HasColumnName("end_time");

        builder.HasIndex(a => new { a.TeacherId, a.DayOfWeek });
    }
}
