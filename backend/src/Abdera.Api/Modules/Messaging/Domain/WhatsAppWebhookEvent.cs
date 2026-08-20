namespace Abdera.Api.Modules.Messaging.Domain;

public enum WebhookEventStatus
{
    Received,
    Processed,
    Failed,
}

// docs/03-erd.md - Messaging > whatsapp_webhook_events. UNIQUE(provider_event_id) - Meta
// aynı olayı tekrar gönderse de tek kayıt (docs/06-whatsapp.md idempotency özeti).
public class WhatsAppWebhookEvent
{
    public Guid Id { get; private set; }
    public string ProviderEventId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public WebhookEventStatus Status { get; private set; } = WebhookEventStatus.Received;
    public string? ProcessingError { get; private set; }

    private WhatsAppWebhookEvent() { }

    public static WhatsAppWebhookEvent Receive(string providerEventId, string eventType, string payloadJson, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ProviderEventId = providerEventId,
        EventType = eventType,
        PayloadJson = payloadJson,
        ReceivedAt = now,
        Status = WebhookEventStatus.Received,
    };

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = WebhookEventStatus.Processed;
        ProcessedAt = now;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Status = WebhookEventStatus.Failed;
        ProcessingError = error;
        ProcessedAt = now;
    }
}
