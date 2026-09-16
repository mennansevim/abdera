using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class PrepayDiscountTierConfiguration : IEntityTypeConfiguration<PrepayDiscountTier>
{
    public void Configure(EntityTypeBuilder<PrepayDiscountTier> builder)
    {
        builder.ToTable("prepay_discount_tiers");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.MinMonths).HasColumnName("min_months");
        builder.Property(t => t.Percent).HasColumnName("percent").HasColumnType("numeric(5,2)");

        // Aynı ay sayısı için iki farklı oran tanımlanamaz - hangisinin geçerli olduğu
        // belirsiz kalırdı.
        builder.HasIndex(t => t.MinMonths).IsUnique();
        builder.ToTable(t => t.HasCheckConstraint("CK_prepay_tiers_months", "min_months BETWEEN 2 AND 24"));
        builder.ToTable(t => t.HasCheckConstraint("CK_prepay_tiers_percent", "percent >= 0 AND percent <= 100"));
    }
}
