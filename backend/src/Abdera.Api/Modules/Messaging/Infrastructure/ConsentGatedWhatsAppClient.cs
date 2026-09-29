using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Infrastructure;

// Velinin "WhatsApp bildirimi alsın" onayı (guardians.notification_consent, varsayılan kapalı)
// kapalıysa ona hiçbir mesaj gitmez - hatırlatma, bot yanıtı, giriş kodu, şifre dahil.
// Kural tek tek çağıran yerlerde değil burada, gönderimin geçtiği tek noktada zorlanır ki
// yeni bir gönderim yolu eklendiğinde unutulamasın. Numarası hiçbir veliye ait olmayan alıcıya
// da gönderilmez: sistem yalnızca velilere yazar.
public class ConsentGatedWhatsAppClient(IWhatsAppClient inner, AbderaDbContext db, ILogger<ConsentGatedWhatsAppClient> logger)
    : IWhatsAppClient
{
    public const string ConsentDisabledError = "Velinin WhatsApp bildirim onayı kapalı.";

    public async Task<WhatsAppSendResult> SendTemplateAsync(
        string toPhoneNumber,
        string templateName,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string>? buttonPayloads = null,
        CancellationToken cancellationToken = default)
    {
        if (!await HasConsentAsync(toPhoneNumber, cancellationToken)) return Blocked(templateName);
        return await inner.SendTemplateAsync(toPhoneNumber, templateName, parameters, buttonPayloads, cancellationToken);
    }

    public async Task<WhatsAppSendResult> SendFreeTextAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default)
    {
        if (!await HasConsentAsync(toPhoneNumber, cancellationToken)) return Blocked("free_text");
        return await inner.SendFreeTextAsync(toPhoneNumber, body, cancellationToken);
    }

    private Task<bool> HasConsentAsync(string phoneNumber, CancellationToken cancellationToken) =>
        db.Guardians.AsNoTracking().AnyAsync(g => g.PhoneNumber == phoneNumber && g.NotificationConsent, cancellationToken);

    private WhatsAppSendResult Blocked(string messageKind)
    {
        // Telefon numarası loglanmaz (CLAUDE.md safe logging).
        logger.LogInformation("WhatsApp gönderimi atlandı: bildirim onayı kapalı ({Kind}).", messageKind);
        return new WhatsAppSendResult(false, null, ConsentDisabledError);
    }
}
