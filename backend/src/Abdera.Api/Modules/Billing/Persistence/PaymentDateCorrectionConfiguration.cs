using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class PaymentDateCorrectionConfiguration : IEntityTypeConfiguration<PaymentDateCorrection>
{
    public void Configure(EntityTypeBuilder<PaymentDateCorrection> builder)
    {
        builder.ToTable("payment_date_corrections");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.PaymentId).HasColumnName("payment_id");
        builder.Property(item => item.PreviousDate).HasColumnName("previous_date");
        builder.Property(item => item.CorrectedDate).HasColumnName("corrected_date");
        builder.Property(item => item.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(item => item.CreatedBy).HasColumnName("created_by");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at");

        builder.HasOne<Payment>().WithMany().HasForeignKey(item => item.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.PaymentId, item.CreatedAt });
        builder.ToTable(table => table.HasCheckConstraint("CK_payment_date_corrections_changed", "corrected_date <> previous_date"));
    }
}
