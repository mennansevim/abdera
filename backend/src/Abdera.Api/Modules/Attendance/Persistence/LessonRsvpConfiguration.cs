using Abdera.Api.Modules.Attendance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Attendance.Persistence;

public class LessonRsvpConfiguration : IEntityTypeConfiguration<LessonRsvp>
{
    public void Configure(EntityTypeBuilder<LessonRsvp> builder)
    {
        builder.ToTable("lesson_rsvps");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.LessonId).HasColumnName("lesson_id");
        builder.Property(r => r.GuardianId).HasColumnName("guardian_id");
        builder.Property(r => r.Response).HasColumnName("response").HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.RespondedAt).HasColumnName("responded_at");
        builder.Property(r => r.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(r => new { r.LessonId, r.GuardianId }).IsUnique();
    }
}
