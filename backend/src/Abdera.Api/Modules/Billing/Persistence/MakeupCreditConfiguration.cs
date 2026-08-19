using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class MakeupCreditConfiguration : IEntityTypeConfiguration<MakeupCredit>
{
    public void Configure(EntityTypeBuilder<MakeupCredit> builder)
    {
        builder.ToTable("makeup_credits");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.StudentId).HasColumnName("student_id");
        builder.Property(c => c.SourceLessonId).HasColumnName("source_lesson_id");
        builder.Property(c => c.EarnedReason).HasColumnName("earned_reason").HasConversion<string>().HasMaxLength(30);
        builder.Property(c => c.EarnedAt).HasColumnName("earned_at");
        builder.Property(c => c.ExpiresAt).HasColumnName("expires_at");
        builder.Property(c => c.UsedLessonId).HasColumnName("used_lesson_id");
        builder.Property(c => c.UsedAt).HasColumnName("used_at");
        builder.Property(c => c.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(c => new { c.StudentId, c.Status });
    }
}
