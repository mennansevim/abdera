using Abdera.Api.Modules.People.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.People.Persistence;

public class GuardianConfiguration : IEntityTypeConfiguration<Guardian>
{
    public void Configure(EntityTypeBuilder<Guardian> builder)
    {
        builder.ToTable("guardians");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id");
        builder.Property(g => g.FirstName).HasColumnName("first_name").HasMaxLength(100).IsRequired();
        builder.Property(g => g.LastName).HasColumnName("last_name").HasMaxLength(100).IsRequired();
        builder.Property(g => g.PhoneNumber).HasColumnName("phone_number").HasMaxLength(20).IsRequired();
        builder.Property(g => g.WhatsappEnabled).HasColumnName("whatsapp_enabled").HasDefaultValue(true);
        builder.Property(g => g.NotificationConsent).HasColumnName("notification_consent").HasDefaultValue(true);
        builder.Property(g => g.ConsentUpdatedAt).HasColumnName("consent_updated_at");
        builder.Property(g => g.ConversationWindowExpiresAt).HasColumnName("conversation_window_expires_at");
        builder.Property(g => g.CreatedAt).HasColumnName("created_at");
        builder.Property(g => g.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(g => g.PhoneNumber).IsUnique();
    }
}
