using Abdera.Api.Modules.People.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.People.Persistence;

public class CustomBranchCategoryConfiguration : IEntityTypeConfiguration<CustomBranchCategory>
{
    public void Configure(EntityTypeBuilder<CustomBranchCategory> builder)
    {
        builder.ToTable("branch_categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.Key).HasColumnName("key").HasMaxLength(40).IsRequired();
        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(60).IsRequired();
        builder.Property(c => c.UnitLabel).HasColumnName("unit_label").HasMaxLength(40).IsRequired();
        builder.Property(c => c.UnitLabelPlural).HasColumnName("unit_label_plural").HasMaxLength(40).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");
        builder.HasIndex(c => c.Key).IsUnique();
        builder.HasIndex(c => c.Name).IsUnique();
    }
}
