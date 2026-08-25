using Abdera.Api.Modules.Banking.Domain;

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
    private static readonly string[] PlaceholderFragments =
        ["<", "change-me", "changeme", "example", "devsecret", "password"];

    public static void EnsureConfigured(WebApplication app)
    {
        if (!app.Environment.IsProduction())
        {
            return;
        }

        var missing = new List<string>();
        RequireProductionProvider(app.Configuration, "WhatsApp:Provider", "Cloud", "WhatsApp__Provider", missing);
        RequireProductionProvider(app.Configuration, "Backup:Provider", "Sftp", "Backup__Provider", missing);

        // Banking: Fake production'da yasak (sahte IBAN gerçek bir veliye verilirse para
        // hiçbir yere gitmez ve kimse fark etmez). 'Manual' geçerli bir production seçimidir -
        // banka entegrasyonu kapalıdır, admin ödemeyi elle girer; webhook hiç kullanılmadığı
        // için paylaşılan sır da beklenmez. Yalnızca gerçek bir sağlayıcı seçiliyken
        // Banking__WebhookSharedSecret zorunlu olur.
        var bankingProvider = app.Configuration["Banking:Provider"];
        if (!BankingProviderModes.IsAllowedInProduction(bankingProvider))
        {
            missing.Add($"Banking__Provider (gerçek sağlayıcı veya '{BankingProviderModes.Manual}')");
        }
        else if (BankingProviderModes.UsesWebhooks(bankingProvider) &&
                 string.IsNullOrWhiteSpace(app.Configuration["Banking:WebhookSharedSecret"]))
        {
            missing.Add("Banking__WebhookSharedSecret");
        }

        if (string.IsNullOrEmpty(app.Configuration["WhatsApp:AppSecret"]))
        {
            missing.Add("WhatsApp__AppSecret");
        }
        if (string.IsNullOrEmpty(app.Configuration["WhatsApp:PayloadSigningKey"]))
        {
            missing.Add("WhatsApp__PayloadSigningKey");
        }
        if (string.Equals(app.Configuration["WhatsApp:Provider"], "Cloud", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(app.Configuration["WhatsApp:PhoneNumberId"]))
            {
                missing.Add("WhatsApp__PhoneNumberId");
            }
            if (string.IsNullOrEmpty(app.Configuration["WhatsApp:AccessToken"]))
            {
                missing.Add("WhatsApp__AccessToken");
            }
            if (string.IsNullOrEmpty(app.Configuration["WhatsApp:WebhookVerifyToken"]))
            {
                missing.Add("WhatsApp__WebhookVerifyToken");
            }
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

        // AI (Faz 10) OPSİYONELDİR - yapılandırılmamış olması production'ı engellemez.
        // Ama sağlayıcı açıkça seçilmişken anahtar boşsa özellik sessizce hiç çalışmaz
        // (yukarıdaki WhatsApp/Backup ile aynı gerekçe: erken ve açık hata).
        if (string.Equals(app.Configuration["Ai:Provider"], "OpenAi", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(app.Configuration["Ai:ApiKey"]))
        {
            missing.Add("Ai__ApiKey");
        }

        RejectPlaceholder(app.Configuration, "WhatsApp:AppSecret", "WhatsApp__AppSecret", missing);
        RejectPlaceholder(app.Configuration, "Ai:ApiKey", "Ai__ApiKey", missing);
        RejectPlaceholder(app.Configuration, "WhatsApp:PayloadSigningKey", "WhatsApp__PayloadSigningKey", missing);
        RejectPlaceholder(app.Configuration, "WhatsApp:AccessToken", "WhatsApp__AccessToken", missing);
        RejectPlaceholder(app.Configuration, "Backup:EncryptionKey", "Backup__EncryptionKey", missing);
        RejectPlaceholder(app.Configuration, "Banking:WebhookSharedSecret", "Banking__WebhookSharedSecret", missing);
        RejectPlaceholder(app.Configuration, "Bootstrap:AdminPassword", "Bootstrap__AdminPassword", missing);

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Production ortamında zorunlu ortam değişkenleri tanımsız: {string.Join(", ", missing)}. " +
                "Bunlar boşken ilgili özellik (WhatsApp gönderimi/imza doğrulaması, yedekleme veya e-posta alarmı) " +
                "sessizce fail-closed olur ya da hiç çalışmaz - uygulama bu yüzden başlamayı reddediyor.");
        }
    }

    private static void RequireProductionProvider(
        IConfiguration configuration, string key, string requiredValue, string environmentName, List<string> errors)
    {
        if (!string.Equals(configuration[key], requiredValue, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{environmentName}={requiredValue}");
        }
    }

    private static void RejectPlaceholder(
        IConfiguration configuration, string key, string environmentName, List<string> errors)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value)) return;
        if (PlaceholderFragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"{environmentName} (placeholder olamaz)");
        }
    }
}
