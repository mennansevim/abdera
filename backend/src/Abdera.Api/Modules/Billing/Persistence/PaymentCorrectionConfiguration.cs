using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class PaymentCorrectionConfiguration : IEntityTypeConfiguration<PaymentCorrection>
{
    public void Configure(EntityTypeBuilder<PaymentCorrection> builder)
    {
        builder.ToTable("payment_corrections");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.PaymentId).HasColumnName("payment_id");
        builder.Property(item => item.PreviousAmount).HasColumnName("previous_amount").HasColumnType("numeric(12,2)");
        builder.Property(item => item.CorrectedAmount).HasColumnName("corrected_amount").HasColumnType("numeric(12,2)");
        builder.Property(item => item.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(item => item.CreatedBy).HasColumnName("created_by");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at");

        builder.HasOne<Payment>().WithMany().HasForeignKey(item => item.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.PaymentId, item.CreatedAt });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_payment_corrections_previous_amount", "previous_amount >= 0");
            table.HasCheckConstraint("CK_payment_corrections_corrected_amount", "corrected_amount >= 0");
        });
    }
}
