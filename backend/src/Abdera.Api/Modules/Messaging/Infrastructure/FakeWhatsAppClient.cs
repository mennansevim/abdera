using Abdera.Api.Modules.Messaging.Domain;

namespace Abdera.Api.Modules.Messaging.Infrastructure;

// WhatsApp__Provider=Fake (dev/test varsayılanı). Gerçek bir API çağrısı yapmaz;
// mesajı loglar ki RSVP/hatırlatma akışı Meta hesabı olmadan uçtan uca izlenebilsin.
// whatsapp_messages tablosuna yazma kısmı Phase 5'te Messaging modülü tamamlanınca eklenir.
public class FakeWhatsAppClient(ILogger<FakeWhatsAppClient> logger) : IWhatsAppClient
{
    public Task<WhatsAppSendResult> SendTemplateAsync(
        string toPhoneNumber,
        string templateName,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        var paramSummary = string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"));
        logger.LogInformation(
            "[FakeWhatsApp] -> {Phone} | şablon={Template} | {Params}",
            toPhoneNumber, templateName, paramSummary);

        return Task.FromResult(new WhatsAppSendResult(
            Success: true,
            ProviderMessageId: $"fake-{Guid.NewGuid()}",
            Error: null));
    }
}
