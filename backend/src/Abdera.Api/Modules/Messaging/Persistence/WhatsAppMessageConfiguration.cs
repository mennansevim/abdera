using Abdera.Api.Modules.Messaging.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Messaging.Persistence;

public class WhatsAppMessageConfiguration : IEntityTypeConfiguration<WhatsAppMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppMessage> builder)
    {
        builder.ToTable("whatsapp_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.NotificationJobId).HasColumnName("notification_job_id");
        builder.Property(m => m.GuardianId).HasColumnName("guardian_id");
        builder.Property(m => m.Direction).HasColumnName("direction").HasConversion<string>().HasMaxLength(10);
        builder.Property(m => m.TemplateId).HasColumnName("template_id");
        builder.Property(m => m.BodySnapshot).HasColumnName("body_snapshot");
        builder.Property(m => m.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(200);
        builder.Property(m => m.SentAt).HasColumnName("sent_at");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(m => m.GuardianId);
        builder.HasIndex(m => m.NotificationJobId);
    }
}
