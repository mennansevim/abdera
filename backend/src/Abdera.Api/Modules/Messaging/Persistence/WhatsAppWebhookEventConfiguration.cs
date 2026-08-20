using Abdera.Api.Modules.Messaging.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Abdera.Api.Modules.Messaging.Persistence;

public class WhatsAppWebhookEventConfiguration : IEntityTypeConfiguration<WhatsAppWebhookEvent>
{
    public void Configure(EntityTypeBuilder<WhatsAppWebhookEvent> builder)
    {
        builder.ToTable("whatsapp_webhook_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.ProviderEventId).HasColumnName("provider_event_id").HasMaxLength(200);
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(50);
        builder.Property(e => e.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        builder.Property(e => e.ReceivedAt).HasColumnName("received_at");
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ProcessingError).HasColumnName("processing_error");

        builder.HasIndex(e => e.ProviderEventId).IsUnique();
    }
}
