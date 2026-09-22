using Abdera.Api.Modules.Messaging.Domain;

namespace Abdera.Api.Modules.Messaging.Infrastructure;

// Production'da WhatsApp henüz kurulmadığında Fake sağlayıcı gibi başarı taklidi yapmaz.
// Çağıran akış gönderimi açık bir hata olarak kaydeder ve kullanıcıya yanıltıcı biçimde
// "mesaj gönderildi" sonucu dönmez.
public class DisabledWhatsAppClient : IWhatsAppClient
{
    private const string DisabledError = "WhatsApp entegrasyonu şu anda devre dışı.";

    public Task<WhatsAppSendResult> SendTemplateAsync(
        string toPhoneNumber,
        string templateName,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string>? buttonPayloads = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WhatsAppSendResult(false, null, DisabledError));

    public Task<WhatsAppSendResult> SendFreeTextAsync(
        string toPhoneNumber,
        string body,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new WhatsAppSendResult(false, null, DisabledError));
}
