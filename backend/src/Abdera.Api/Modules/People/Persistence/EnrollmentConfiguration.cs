using Abdera.Api.Modules.People.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.People.Persistence;

public class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("enrollments");
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_enrollments_manual_discount_percent",
            "manual_discount_percent IS NULL OR (manual_discount_percent >= 0 AND manual_discount_percent <= 100)"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.StudentId).HasColumnName("student_id");
        builder.Property(e => e.TeacherId).HasColumnName("teacher_id");
        builder.Property(e => e.InstrumentId).HasColumnName("instrument_id");
        builder.Property(e => e.CourseKind).HasColumnName("course_kind").HasConversion<string>().HasMaxLength(20).HasDefaultValue(CourseKind.Individual);
        builder.Property(e => e.ManualDiscountPercent).HasColumnName("manual_discount_percent").HasColumnType("numeric(5,2)");
        builder.Property(e => e.ManualDiscountReason).HasColumnName("manual_discount_reason").HasMaxLength(200);
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.StartedAt).HasColumnName("started_at");
        builder.Property(e => e.EndedAt).HasColumnName("ended_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne<Student>().WithMany().HasForeignKey(e => e.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Teacher>().WithMany().HasForeignKey(e => e.TeacherId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Instrument>().WithMany().HasForeignKey(e => e.InstrumentId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.StudentId);
        builder.HasIndex(e => e.TeacherId);
        builder.HasIndex(e => new { e.StudentId, e.TeacherId, e.InstrumentId })
            .IsUnique()
            .HasFilter("status = 'Active'");
    }
}
