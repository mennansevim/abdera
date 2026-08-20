using Abdera.Api.Modules.Banking.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Banking.Persistence;

public class VirtualIbanConfiguration : IEntityTypeConfiguration<VirtualIban>
{
    public void Configure(EntityTypeBuilder<VirtualIban> builder)
    {
        builder.ToTable("virtual_ibans");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id");
        builder.Property(v => v.GuardianId).HasColumnName("guardian_id");
        builder.Property(v => v.Iban).HasColumnName("iban").HasMaxLength(34).IsRequired();
        builder.Property(v => v.Provider).HasColumnName("provider").HasMaxLength(50).IsRequired();
        builder.Property(v => v.ProviderReference).HasColumnName("provider_reference").HasMaxLength(200);
        builder.Property(v => v.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.CreatedAt).HasColumnName("created_at");
        builder.Property(v => v.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(v => v.Iban).IsUnique();
        // docs/12-bank-integration.md: bir veliye aynı anda birden fazla Active sanal IBAN
        // atanamaz. Genel bir UNIQUE(guardian_id) kısıtı Inactive geçmişi de engelleyeceği
        // için burada uygulanmaz - kontrol AssignVirtualIban.cs'de (bkz. price_list_items
        // çakışma kontrolü örneği, CLAUDE.md).
        builder.HasIndex(v => v.GuardianId);
    }
}
