using Abdera.Api.Modules.Messaging.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Messaging.Persistence;

public class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        builder.ToTable("message_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");
        builder.Property(t => t.Name).HasColumnName("name").HasMaxLength(100);
        builder.Property(t => t.Language).HasColumnName("language").HasMaxLength(5).HasDefaultValue("tr");
        builder.Property(t => t.Body).HasColumnName("body");
        builder.Property(t => t.IsActive).HasColumnName("is_active").HasDefaultValue(true);

        builder.HasIndex(t => t.Name).IsUnique();
    }
}
