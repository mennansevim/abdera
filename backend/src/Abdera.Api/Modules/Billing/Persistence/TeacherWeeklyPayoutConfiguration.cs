using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class TeacherWeeklyPayoutConfiguration : IEntityTypeConfiguration<TeacherWeeklyPayout>
{
    public void Configure(EntityTypeBuilder<TeacherWeeklyPayout> builder)
    {
        builder.ToTable("teacher_weekly_payouts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.TeacherId).HasColumnName("teacher_id");
        builder.Property(p => p.WeekStart).HasColumnName("week_start");
        builder.Property(p => p.WeekEnd).HasColumnName("week_end");
        builder.Property(p => p.LessonCount).HasColumnName("lesson_count");
        builder.Property(p => p.RatePerLesson).HasColumnName("rate_per_lesson").HasColumnType("numeric(12,2)");
        builder.Property(p => p.ComputedAmount).HasColumnName("computed_amount").HasColumnType("numeric(12,2)");
        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)");
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("TRY");
        builder.Property(p => p.PaidOn).HasColumnName("paid_on");
        builder.Property(p => p.Note).HasColumnName("note");
        builder.Property(p => p.ExpenseId).HasColumnName("expense_id");
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");

        // Çift ödemenin tek gerçek engeli: aynı öğretmene aynı hafta için ikinci satır
        // açılamaz. Uygulama katmanı da kontrol eder ama yarışı DB kapatır.
        builder.HasIndex(p => new { p.TeacherId, p.WeekStart })
            .IsUnique()
            .HasDatabaseName("ix_teacher_weekly_payouts_teacher_week");
        builder.HasIndex(p => p.WeekStart);

        builder.ToTable(t => t.HasCheckConstraint("CK_teacher_weekly_payouts_amount", "amount > 0"));
        builder.ToTable(t => t.HasCheckConstraint("CK_teacher_weekly_payouts_lessons", "lesson_count > 0"));
        builder.ToTable(t => t.HasCheckConstraint("CK_teacher_weekly_payouts_week", "week_end > week_start"));
    }
}
