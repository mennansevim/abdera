using Abdera.Api.Modules.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Billing.Persistence;

public class BillingSettingsConfiguration : IEntityTypeConfiguration<BillingSettings>
{
    public void Configure(EntityTypeBuilder<BillingSettings> builder)
    {
        builder.ToTable("billing_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");
        builder.Property(s => s.MultiCourseDiscountPercent).HasColumnName("multi_course_discount_percent").HasColumnType("numeric(5,2)");
        builder.Property(s => s.SiblingDiscountPercent).HasColumnName("sibling_discount_percent").HasColumnType("numeric(5,2)");
        builder.Property(s => s.DueDayOfMonth).HasColumnName("due_day_of_month");
        builder.Property(s => s.UpdatedBy).HasColumnName("updated_by");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at");

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_billing_settings_due_day", "due_day_of_month BETWEEN 1 AND 28"));
    }
}
