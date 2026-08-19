namespace Abdera.Api.Modules.Messaging.Domain;

// docs/06-whatsapp.md / docs/10-decisions.md D2: Meta WABA onayı haftalar sürebilir,
// bu yüzden gönderim iki implementasyon arkasına gizlenir. Gerçek NotificationJob/
// WhatsAppMessage tabloları ve Cloud API implementasyonu Phase 5'te gelir - burada
// yalnızca sınır (port) ve dev'de kullanılan Fake implementasyon var.
public interface IWhatsAppClient
{
    Task<WhatsAppSendResult> SendTemplateAsync(
        string toPhoneNumber,
        string templateName,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);
}

public record WhatsAppSendResult(bool Success, string? ProviderMessageId, string? Error);
