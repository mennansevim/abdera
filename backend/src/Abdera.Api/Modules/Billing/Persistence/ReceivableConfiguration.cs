using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class ReceivableConfiguration : IEntityTypeConfiguration<Receivable>
{
    public void Configure(EntityTypeBuilder<Receivable> builder)
    {
        builder.ToTable("receivables");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.EnrollmentId).HasColumnName("enrollment_id");
        builder.Property(r => r.TuitionRateId).HasColumnName("tuition_rate_id");
        builder.Property(r => r.Period).HasColumnName("period").HasMaxLength(20);
        builder.Property(r => r.BaseAmount).HasColumnName("base_amount").HasColumnType("numeric(12,2)");
        builder.Property(r => r.DiscountPercent).HasColumnName("discount_percent").HasColumnType("numeric(5,2)");
        builder.Property(r => r.DiscountReason).HasColumnName("discount_reason").HasMaxLength(200);
        builder.Property(r => r.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)");
        builder.Property(r => r.PrepayPlanId).HasColumnName("prepay_plan_id");
        builder.Property(r => r.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("TRY");
        builder.Property(r => r.DueDate).HasColumnName("due_date");
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(r => new { r.EnrollmentId, r.Period }).IsUnique();
        builder.HasIndex(r => r.Status);

        builder.HasIndex(r => r.PrepayPlanId).HasFilter("prepay_plan_id IS NOT NULL");

        builder.ToTable(t => t.HasCheckConstraint("CK_receivables_amount", "amount >= 0"));
        builder.ToTable(t => t.HasCheckConstraint("CK_receivables_base_amount", "base_amount >= 0"));
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_receivables_discount_percent", "discount_percent >= 0 AND discount_percent <= 100"));

        // ARC-1 (docs/13-audit-fix-prompt.md): iki admin aynı Receivable'a aynı anda ödeme
        // işlerse ikinci yazma birincisini sessizce ezmesin diye Postgres'in sistem kolonu
        // xmin'i optimistic concurrency token olarak kullanıyoruz - ek kolon gerekmez.
        // Npgsql.EntityFrameworkCore.PostgreSQL 7.0'dan itibaren UseXminAsConcurrencyToken()
        // kaldırıldı; standart EF mekanizması olan uint + IsRowVersion() kullanılıyor,
        // sağlayıcı bunu otomatik olarak xmin sistem kolonuna eşliyor (bkz.
        // https://www.npgsql.org/efcore/modeling/concurrency.html). Domain entity'sine
        // dokunmamak için shadow property olarak tanımlanıyor.
        builder.Property<uint>("Version").IsRowVersion();
    }
}
