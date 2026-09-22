using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.ReceivableId).HasColumnName("receivable_id");
        builder.Property(p => p.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)");
        builder.Property(p => p.PaymentDate).HasColumnName("payment_date");
        builder.Property(p => p.Method).HasColumnName("method").HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Reference).HasColumnName("reference").HasMaxLength(200);
        builder.Property(p => p.Note).HasColumnName("note");
        builder.Property(p => p.PrepayPlanId).HasColumnName("prepay_plan_id");
        builder.Property(p => p.PrepayPlanMonths).HasColumnName("prepay_plan_months");
        builder.Property(p => p.CreatedBy).HasColumnName("created_by");
        builder.Property(p => p.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(100);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(p => p.ReceivableId);
        builder.HasIndex(p => p.PrepayPlanId);
        builder.HasIndex(p => p.IdempotencyKey)
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName("ux_payments_idempotency_key");
        builder.ToTable(t => t.HasCheckConstraint("CK_payments_amount", "amount > 0"));
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_payments_prepay_plan_months",
            "(prepay_plan_id IS NULL AND prepay_plan_months IS NULL) OR (prepay_plan_id IS NOT NULL AND prepay_plan_months BETWEEN 2 AND 24)"));
    }
}
