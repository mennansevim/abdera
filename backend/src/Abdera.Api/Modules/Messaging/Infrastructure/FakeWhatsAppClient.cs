using Abdera.Api.Modules.Messaging.Domain;

namespace Abdera.Api.Modules.Messaging.Infrastructure;

// WhatsApp__Provider=Fake (dev/test varsayılanı). Gerçek bir API çağrısı yapmaz;
// mesajı loglar ki RSVP/hatırlatma akışı Meta hesabı olmadan uçtan uca izlenebilsin.
// whatsapp_messages tablosuna yazma NotificationDispatcher/Webhooks.cs tarafında yapılır -
// bu client yalnızca "gönderim başarılı oldu mu" sorusuna cevap verir.
public class FakeWhatsAppClient(ILogger<FakeWhatsAppClient> logger) : IWhatsAppClient
{
    public Task<WhatsAppSendResult> SendTemplateAsync(
        string toPhoneNumber,
        string templateName,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string>? buttonPayloads = null,
        CancellationToken cancellationToken = default)
    {
        var paramSummary = string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"));
        var buttonSummary = buttonPayloads is { Count: > 0 } ? $" | butonlar={string.Join(" | ", buttonPayloads)}" : "";
        logger.LogInformation(
            "[FakeWhatsApp] şablon -> {Phone} | şablon={Template} | {Params}{Buttons}",
            toPhoneNumber, templateName, paramSummary, buttonSummary);

        return Task.FromResult(new WhatsAppSendResult(
            Success: true,
            ProviderMessageId: $"fake-{Guid.NewGuid()}",
            Error: null));
    }

    public Task<WhatsAppSendResult> SendFreeTextAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[FakeWhatsApp] serbest metin -> {Phone} | {Body}", toPhoneNumber, body);

        return Task.FromResult(new WhatsAppSendResult(
            Success: true,
            ProviderMessageId: $"fake-{Guid.NewGuid()}",
            Error: null));
    }
}
