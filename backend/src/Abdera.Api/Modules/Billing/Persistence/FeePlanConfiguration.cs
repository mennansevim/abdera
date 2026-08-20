using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class FeePlanConfiguration : IEntityTypeConfiguration<FeePlan>
{
    public void Configure(EntityTypeBuilder<FeePlan> builder)
    {
        builder.ToTable("fee_plans");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");
        builder.Property(f => f.EnrollmentId).HasColumnName("enrollment_id");
        builder.Property(f => f.PriceListItemId).HasColumnName("price_list_item_id");
        builder.Property(f => f.BillingType).HasColumnName("billing_type").HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)");
        builder.Property(f => f.Currency).HasColumnName("currency").HasMaxLength(3).HasDefaultValue("TRY");
        builder.Property(f => f.DueDay).HasColumnName("due_day");
        builder.Property(f => f.PackageLessonCount).HasColumnName("package_lesson_count");
        builder.Property(f => f.ActiveFrom).HasColumnName("active_from");
        builder.Property(f => f.ActiveUntil).HasColumnName("active_until");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(f => f.EnrollmentId);
    }
}
