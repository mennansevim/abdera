using Abdera.Api.Modules.People.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.People.Persistence;

public class StudentGuardianConfiguration : IEntityTypeConfiguration<StudentGuardian>
{
    public void Configure(EntityTypeBuilder<StudentGuardian> builder)
    {
        builder.ToTable("student_guardians");
        builder.HasKey(sg => new { sg.StudentId, sg.GuardianId });
        builder.Property(sg => sg.StudentId).HasColumnName("student_id");
        builder.Property(sg => sg.GuardianId).HasColumnName("guardian_id");
        builder.Property(sg => sg.Relationship).HasColumnName("relationship").HasMaxLength(50);
        builder.Property(sg => sg.IsPrimary).HasColumnName("is_primary").HasDefaultValue(false);

        builder.HasOne<Student>().WithMany().HasForeignKey(sg => sg.StudentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Guardian>().WithMany().HasForeignKey(sg => sg.GuardianId).OnDelete(DeleteBehavior.Cascade);
    }
}
