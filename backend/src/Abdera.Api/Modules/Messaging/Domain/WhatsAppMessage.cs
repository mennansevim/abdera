namespace Abdera.Api.Modules.Messaging.Domain;

public enum MessageDirection
{
    Outbound,
    Inbound,
}

// docs/03-erd.md - Messaging > whatsapp_messages. Giden/gelen her mesajın gerçek metin
// kopyası burada tutulur - şablon sonradan değişse/onaydan düşse bile geçmiş mesaj bozulmaz.
public class WhatsAppMessage
{
    public Guid Id { get; private set; }
    public Guid? NotificationJobId { get; private set; }
    public Guid GuardianId { get; private set; }
    public MessageDirection Direction { get; private set; }
    public Guid? TemplateId { get; private set; }
    public string BodySnapshot { get; private set; } = null!;
    public string? ProviderMessageId { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private WhatsAppMessage() { }

    public static WhatsAppMessage CreateOutbound(
        Guid? notificationJobId, Guid guardianId, Guid? templateId, string bodySnapshot,
        string? providerMessageId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        NotificationJobId = notificationJobId,
        GuardianId = guardianId,
        Direction = MessageDirection.Outbound,
        TemplateId = templateId,
        BodySnapshot = bodySnapshot,
        ProviderMessageId = providerMessageId,
        SentAt = now,
        CreatedAt = now,
    };

    public static WhatsAppMessage CreateInbound(Guid guardianId, string bodySnapshot, string? providerMessageId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        GuardianId = guardianId,
        Direction = MessageDirection.Inbound,
        BodySnapshot = bodySnapshot,
        ProviderMessageId = providerMessageId,
        CreatedAt = now,
    };
}
