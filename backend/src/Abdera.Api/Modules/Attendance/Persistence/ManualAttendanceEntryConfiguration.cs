using Abdera.Api.Modules.Attendance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Attendance.Persistence;

public class ManualAttendanceEntryConfiguration : IEntityTypeConfiguration<ManualAttendanceEntry>
{
    public void Configure(EntityTypeBuilder<ManualAttendanceEntry> builder)
    {
        builder.ToTable("manual_attendance_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.EnrollmentId).HasColumnName("enrollment_id");
        builder.Property(e => e.Date).HasColumnName("date");
        builder.Property(e => e.CreatedBy).HasColumnName("created_by");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        // Aynı kayda aynı gün iki kez yazılamaz - bir satır bir derstir.
        builder.HasIndex(e => new { e.EnrollmentId, e.Date }).IsUnique();
    }
}
