using Abdera.Api.Modules.Scheduling.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Scheduling.Persistence;

public class TeacherTimeOffConfiguration : IEntityTypeConfiguration<TeacherTimeOff>
{
    public void Configure(EntityTypeBuilder<TeacherTimeOff> builder)
    {
        builder.ToTable("teacher_time_off");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.TeacherId).HasColumnName("teacher_id");
        builder.Property(t => t.StartsOn).HasColumnName("starts_on");
        builder.Property(t => t.EndsOn).HasColumnName("ends_on");
        builder.Property(t => t.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(t => new { t.TeacherId, t.StartsOn, t.EndsOn });
        builder.ToTable(t => t.HasCheckConstraint("CK_teacher_time_off_dates", "ends_on >= starts_on"));
    }
}
