using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Infrastructure;

// Velinin "WhatsApp bildirimi alsın" onayı (guardians.notification_consent, varsayılan kapalı)
// kapalıysa ona hiçbir mesaj gitmez - hatırlatma, bot yanıtı, giriş kodu, şifre dahil.
// Kural tek tek çağıran yerlerde değil burada, gönderimin geçtiği tek noktada zorlanır ki
// yeni bir gönderim yolu eklendiğinde unutulamasın. Numarası hiçbir veliye ait olmayan alıcıya
// da gönderilmez: sistem yalnızca velilere yazar.
//
// Tek istisna (docs/10-decisions.md R2): yeni kaydedilen veliye giden karşılama mesajı
// (welcome_student: panel adresi, şifre içermez). Yalnızca bu şablon ve yalnızca velinin karşılama borcu
// (guardians.welcome_message_pending) açıkken geçer; borcu kapatmak çağıranın işidir
// (Guardians.ResetPasswordAsync). İstisna burada durur ki "onaysız ne gidebilir" sorusunun
// yanıtı tek dosyada kalsın - yeni bir istisna eklemeden önce onay al.
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
        var isWelcome = templateName == WhatsAppTemplateNames.GuardianWelcome;
        if (!await HasConsentAsync(toPhoneNumber, isWelcome, cancellationToken)) return Blocked(templateName);
        return await inner.SendTemplateAsync(toPhoneNumber, templateName, parameters, buttonPayloads, cancellationToken);
    }

    public async Task<WhatsAppSendResult> SendAuthenticationCodeAsync(
        string toPhoneNumber, string templateName, string code, CancellationToken cancellationToken = default)
    {
        if (!await HasConsentAsync(toPhoneNumber, allowPendingWelcome: false, cancellationToken)) return Blocked(templateName);
        return await inner.SendAuthenticationCodeAsync(toPhoneNumber, templateName, code, cancellationToken);
    }

    public async Task<WhatsAppSendResult> SendFreeTextAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default)
    {
        if (!await HasConsentAsync(toPhoneNumber, allowPendingWelcome: false, cancellationToken)) return Blocked("free_text");
        return await inner.SendFreeTextAsync(toPhoneNumber, body, cancellationToken);
    }

    private Task<bool> HasConsentAsync(string phoneNumber, bool allowPendingWelcome, CancellationToken cancellationToken) =>
        db.Guardians.AsNoTracking().AnyAsync(
            g => g.PhoneNumber == phoneNumber && (g.NotificationConsent || (allowPendingWelcome && g.WelcomeMessagePending)),
            cancellationToken);

    private WhatsAppSendResult Blocked(string messageKind)
    {
        // Telefon numarası loglanmaz (CLAUDE.md safe logging).
        logger.LogInformation("WhatsApp gönderimi atlandı: bildirim onayı kapalı ({Kind}).", messageKind);
        return new WhatsAppSendResult(false, null, ConsentDisabledError);
    }
}
