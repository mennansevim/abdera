using Abdera.Api.Modules.Attendance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Attendance.Persistence;

public class LessonAttendanceConfiguration : IEntityTypeConfiguration<LessonAttendance>
{
    public void Configure(EntityTypeBuilder<LessonAttendance> builder)
    {
        builder.ToTable("lesson_attendances");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.LessonId).HasColumnName("lesson_id");
        builder.Property(a => a.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.MarkedByTeacherId).HasColumnName("marked_by_teacher_id");
        builder.Property(a => a.MarkedAt).HasColumnName("marked_at");
        builder.Property(a => a.Note).HasColumnName("note");

        builder.HasIndex(a => a.LessonId).IsUnique();
    }
}
