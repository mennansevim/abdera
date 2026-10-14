using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class TeacherMonthlyGroupPayoutConfiguration : IEntityTypeConfiguration<TeacherMonthlyGroupPayout>
{
    public void Configure(EntityTypeBuilder<TeacherMonthlyGroupPayout> builder)
    {
        builder.ToTable("teacher_monthly_group_payouts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.TeacherId).HasColumnName("teacher_id");
        builder.Property(p => p.Period).HasColumnName("period").HasMaxLength(7).IsRequired();
        builder.Property(p => p.RevenueBase).HasColumnName("revenue_base").HasColumnType("numeric(12,2)");
        builder.Property(p => p.ReceivableCount).HasColumnName("receivable_count");
        builder.Property(p => p.SharePercent).HasColumnName("share_percent").HasColumnType("numeric(5,2)");
        builder.Property(p => p.ComputedAmount).HasColumnName("computed_amount").HasColumnType("numeric(12,2)");
        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)");
        builder.Property(p => p.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("TRY");
        builder.Property(p => p.PaidOn).HasColumnName("paid_on");
        builder.Property(p => p.Note).HasColumnName("note");
        builder.Property(p => p.ExpenseId).HasColumnName("expense_id");
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");

        // Çift ödemenin tek gerçek engeli: aynı öğretmene aynı ay için ikinci grup ödemesi açılamaz.
        builder.HasIndex(p => new { p.TeacherId, p.Period })
            .IsUnique()
            .HasDatabaseName("ix_teacher_monthly_group_payouts_teacher_period");

        builder.ToTable(t => t.HasCheckConstraint("CK_teacher_monthly_group_payouts_amount", "amount > 0"));
        builder.ToTable(t => t.HasCheckConstraint("CK_teacher_monthly_group_payouts_revenue", "revenue_base > 0"));
        builder.ToTable(t => t.HasCheckConstraint("CK_teacher_monthly_group_payouts_share", "share_percent > 0 AND share_percent <= 100"));
    }
}
