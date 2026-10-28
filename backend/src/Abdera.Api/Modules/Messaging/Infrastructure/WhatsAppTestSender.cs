using Abdera.Api.Modules.Messaging.Domain;

namespace Abdera.Api.Modules.Messaging.Infrastructure;

// Yönetici panelindeki "WhatsApp'ı dene" kartının gönderim yolu. Sağlayıcı istemcisini
// ConsentGatedWhatsAppClient'ın ARKASINDAN çağırır: test numarası bir veliye ait değildir, onay kapısı
// onu zaten engellerdi. Kapıyı gevşetmek yerine istisna yalnızca burada, tek bir sabit şablonla
// (welcome_student - şifre/kişisel veri içermez) ve yalnızca Admin uç noktasından (TestMessage) durur.
public class WhatsAppTestSender(IWhatsAppClient provider)
{
    public Task<WhatsAppSendResult> SendAsync(string toPhoneNumber, CancellationToken cancellationToken = default) =>
        provider.SendTemplateAsync(
            toPhoneNumber,
            WhatsAppTemplateNames.GuardianWelcome,
            new Dictionary<string, string> { ["guardian_name"] = "Test" },
            buttonPayloads: null,
            cancellationToken);
}
