namespace Abdera.Api.Modules.Messaging.Domain;

// docs/06-whatsapp.md / docs/10-decisions.md D2: Meta WABA onayı haftalar sürebilir,
// bu yüzden gönderim iki implementasyon arkasına gizlenir. NotificationDispatcher (Phase 5)
// SendTemplateAsync kullanır (zamanlanmış/otomatik bildirimler); SendFreeTextAsync yalnızca
// A7'nin 24 saatlik penceresi açıkken, deterministik intent yanıtları (ders/aidat/telafi)
// için kullanılır - dinamik içerik olduğu için önceden onaylı bir şablona sığmaz.
public interface IWhatsAppClient
{
    Task<WhatsAppSendResult> SendTemplateAsync(
        string toPhoneNumber,
        string templateName,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default);

    Task<WhatsAppSendResult> SendFreeTextAsync(
        string toPhoneNumber,
        string body,
        CancellationToken cancellationToken = default);
}

public record WhatsAppSendResult(bool Success, string? ProviderMessageId, string? Error);
