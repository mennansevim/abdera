using Abdera.Api.Modules.People.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.People.Persistence;

public class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("enrollments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.StudentId).HasColumnName("student_id");
        builder.Property(e => e.TeacherId).HasColumnName("teacher_id");
        builder.Property(e => e.InstrumentId).HasColumnName("instrument_id");
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
    }
}
