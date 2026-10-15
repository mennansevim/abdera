using Abdera.Api.Modules.Instance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Instance.Persistence;

public class SchoolProfileConfiguration : IEntityTypeConfiguration<SchoolProfile>
{
    public void Configure(EntityTypeBuilder<SchoolProfile> builder)
    {
        builder.ToTable("school_profile");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(SchoolProfile.MaxNameLength).IsRequired();
        builder.Property(p => p.ShortName).HasColumnName("short_name").HasMaxLength(SchoolProfile.MaxShortNameLength).IsRequired();
        builder.Property(p => p.PrimaryColor).HasColumnName("primary_color").HasMaxLength(7);
        builder.Property(p => p.Logo).HasColumnName("logo");
        builder.Property(p => p.LogoContentType).HasColumnName("logo_content_type").HasMaxLength(20);
        builder.Property(p => p.LogoVersion).HasColumnName("logo_version");
        builder.Property(p => p.UpdatedBy).HasColumnName("updated_by");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");
        // İki yönetici aynı anda adı/logoyu değiştirirse sessizce üst üste yazılmasın.
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.Ignore(p => p.HasLogo);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_school_profile_logo_consistent",
            "(logo IS NULL AND logo_content_type IS NULL AND logo_version IS NULL) OR " +
            "(logo IS NOT NULL AND logo_content_type IS NOT NULL AND logo_version IS NOT NULL)"));
    }
}
