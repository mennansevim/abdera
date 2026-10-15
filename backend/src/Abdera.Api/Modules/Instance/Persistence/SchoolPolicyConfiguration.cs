using Abdera.Api.Modules.Instance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Instance.Persistence;

public class SchoolPolicyConfiguration : IEntityTypeConfiguration<SchoolPolicy>
{
    public void Configure(EntityTypeBuilder<SchoolPolicy> builder)
    {
        builder.ToTable("school_policy");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.GroupLessonSharePercent).HasColumnName("group_lesson_share_percent").HasColumnType("numeric(5,2)");
        builder.Property(p => p.MaxWeeklyLessons).HasColumnName("max_weekly_lessons");
        builder.Property(p => p.MakeupNoticeHours).HasColumnName("makeup_notice_hours");
        builder.Property(p => p.MakeupCreditValidDays).HasColumnName("makeup_credit_valid_days");
        builder.Property(p => p.ExcusedMakeupWindowDays).HasColumnName("excused_makeup_window_days");
        builder.Property(p => p.SchoolDayStart).HasColumnName("school_day_start");
        builder.Property(p => p.SchoolDayEnd).HasColumnName("school_day_end");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        // Ayarlar ekranında iki yönetici aynı anda kaydederse biri sessizce ezilmesin.
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_school_policy_share", "group_lesson_share_percent >= 0 AND group_lesson_share_percent <= 100");
            t.HasCheckConstraint("CK_school_policy_day", "school_day_end > school_day_start");
            t.HasCheckConstraint("CK_school_policy_positive",
                "max_weekly_lessons > 0 AND makeup_notice_hours >= 0 AND makeup_credit_valid_days > 0 AND excused_makeup_window_days > 0");
        });
    }
}
