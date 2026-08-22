namespace Abdera.Api.Shared;

// SEC-1/SEC-2 (docs/13-audit-fix-prompt.md): WhatsApp__AppSecret ve WhatsApp__PayloadSigningKey
// tanımsız kalırsa webhook imza doğrulaması/RSVP buton imzası artık fail-closed davranıyor
// (bkz. WebhookSignatureVerifier.IsValid, RsvpButtonPayload.TryVerify) - ama "her isteği
// sessizce reddet" bir prod ortamında fark edilmeden uzun süre sürebilir (WhatsApp
// entegrasyonu tamamen çalışmaz hale gelir, kimse haberdar olmaz). Production'da bu
// anahtarlar eksikse uygulama hiç ayağa kalkmasın, hata erken ve açık olsun.
// Development'ta zorunlu değil - Fake WhatsApp sağlayıcısı bu anahtarları hiç kullanmaz.
public static class ProductionSecretsGuard
{
    public static void EnsureConfigured(WebApplication app)
    {
        if (!app.Environment.IsProduction())
        {
            return;
        }

        var missing = new List<string>();
        if (string.IsNullOrEmpty(app.Configuration["WhatsApp:AppSecret"]))
        {
            missing.Add("WhatsApp__AppSecret");
        }
        if (string.IsNullOrEmpty(app.Configuration["WhatsApp:PayloadSigningKey"]))
        {
            missing.Add("WhatsApp__PayloadSigningKey");
        }

        // Faz 4 (docs/10-decisions.md G): Backup__Provider=Sftp/Email__Provider=Smtp
        // Production'da seçiliyken ilgili kimlik bilgileri eksikse yedekleme/alarm sessizce
        // hiç çalışmaz (WhatsApp__AppSecret ile aynı gerekçe - erken ve açık hata tercih edilir).
        if (string.Equals(app.Configuration["Backup:Provider"], "Sftp", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(app.Configuration["Backup:EncryptionKey"]))
            {
                missing.Add("Backup__EncryptionKey");
            }
            if (string.IsNullOrEmpty(app.Configuration["Backup:Sftp:Host"]))
            {
                missing.Add("Backup__Sftp__Host");
            }
            if (string.IsNullOrEmpty(app.Configuration["Backup:Sftp:Password"]) && string.IsNullOrEmpty(app.Configuration["Backup:Sftp:PrivateKeyPath"]))
            {
                missing.Add("Backup__Sftp__Password veya Backup__Sftp__PrivateKeyPath");
            }
        }
        if (string.Equals(app.Configuration["Email:Provider"], "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(app.Configuration["Email:Smtp:Host"]))
            {
                missing.Add("Email__Smtp__Host");
            }
            if (string.IsNullOrEmpty(app.Configuration["Email:Smtp:Password"]))
            {
                missing.Add("Email__Smtp__Password");
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Production ortamında zorunlu ortam değişkenleri tanımsız: {string.Join(", ", missing)}. " +
                "Bunlar boşken ilgili özellik (WhatsApp imza doğrulaması, yedekleme veya e-posta alarmı) " +
                "sessizce fail-closed olur ya da hiç çalışmaz - uygulama bu yüzden başlamayı reddediyor.");
        }
    }
}
