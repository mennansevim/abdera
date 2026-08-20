using Abdera.Api.Modules.Pricing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Pricing.Persistence;

public class PriceListItemConfiguration : IEntityTypeConfiguration<PriceListItem>
{
    public void Configure(EntityTypeBuilder<PriceListItem> builder)
    {
        builder.ToTable("price_list_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");
        builder.Property(i => i.PriceListId).HasColumnName("price_list_id");
        builder.Property(i => i.InstrumentId).HasColumnName("instrument_id");
        builder.Property(i => i.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(i => i.BillingType).HasColumnName("billing_type").HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)");
        builder.Property(i => i.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("TRY");
        builder.Property(i => i.PackageLessonCount).HasColumnName("package_lesson_count");

        builder.HasOne<PriceList>().WithMany().HasForeignKey(i => i.PriceListId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => new { i.InstrumentId, i.DurationMinutes, i.BillingType });

        builder.ToTable(t => t.HasCheckConstraint("CK_price_list_items_amount", "amount >= 0"));
    }
}
