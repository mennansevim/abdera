using Abdera.Api.Modules.Banking.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Ops.Domain;

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
        // Demo modu anonim debug-login gibi yalnızca tanıtım/staging yüzeylerini açar.
        // Yanlış bir production ortam değişkeni gerçek kimlik doğrulamasını baypas eden bir
        // uç yayınlamamalı; rota ayrıca Production'da hiç kaydedilmese de burada fail-fast
        // davranarak hatalı dağıtımı görünür kılıyoruz.
        if (app.Configuration.GetValue<bool>("Demo:Enabled"))
        {
            missing.Add("Demo__Enabled=false");
        }

        // Şifresiz yerel giriş (DevLogin) Production'da zaten haritalanmaz; bayrağın yine de
        // açık gelmesi yanlış bir .env kopyalandığını gösterir, sessizce geçme.
        if (app.Configuration.GetValue<bool>("Auth:DevLogin:Enabled"))
        {
            missing.Add("Auth__DevLogin__Enabled=false");
        }

        if (!app.Configuration.GetValue<bool>("Auth:PersistKeysToDatabase") &&
            string.IsNullOrWhiteSpace(app.Configuration["Auth:KeysDirectory"]))
        {
            missing.Add("Auth__PersistKeysToDatabase=true veya Auth__KeysDirectory");
        }

        var whatsAppProvider = app.Configuration["WhatsApp:Provider"];
        if (!WhatsAppProviderModes.IsAllowedInProduction(whatsAppProvider))
        {
            missing.Add($"WhatsApp__Provider ({WhatsAppProviderModes.Cloud} veya {WhatsAppProviderModes.Disabled})");
        }

        var backupProvider = app.Configuration["Backup:Provider"];
        if (!BackupProviderModes.IsAllowedInProduction(backupProvider))
        {
            missing.Add($"Backup__Provider ({BackupProviderModes.Sftp} veya {BackupProviderModes.Disabled})");
        }

        // Credential taşıyan isteklerin izin verilen origin'i production'da açıkça HTTPS
        // olmalı. localhost varsayılanıyla canlıya çıkmak CORS'u yalnızca bazı istemcilerde
        // bozan, teşhisi zor bir yapılandırma hatasıdır; başlangıçta görünür biçimde reddet.
        var frontendOrigin = app.Configuration["Frontend:Origin"];
        if (!Uri.TryCreate(frontendOrigin, UriKind.Absolute, out var originUri) ||
            originUri.Scheme != Uri.UriSchemeHttps || originUri.IsLoopback)
        {
            missing.Add("Frontend__Origin (https:// production adresi)");
        }

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

        if (string.Equals(whatsAppProvider, WhatsAppProviderModes.Cloud, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(app.Configuration["WhatsApp:AppSecret"]))
            {
                missing.Add("WhatsApp__AppSecret");
            }
            if (string.IsNullOrEmpty(app.Configuration["WhatsApp:PayloadSigningKey"]))
            {
                missing.Add("WhatsApp__PayloadSigningKey");
            }
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
        if (string.Equals(backupProvider, BackupProviderModes.Sftp, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsValidBackupEncryptionKey(app.Configuration["Backup:EncryptionKey"]))
            {
                missing.Add("Backup__EncryptionKey (32 byte AES anahtarı, base64)");
            }
            if (string.IsNullOrEmpty(app.Configuration["Backup:Sftp:Host"]))
            {
                missing.Add("Backup__Sftp__Host");
            }
            if (string.IsNullOrEmpty(app.Configuration["Backup:Sftp:Username"]))
            {
                missing.Add("Backup__Sftp__Username");
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
            if (string.IsNullOrEmpty(app.Configuration["Email:Smtp:Username"]))
            {
                missing.Add("Email__Smtp__Username");
            }
            if (!System.Net.Mail.MailAddress.TryCreate(
                    app.Configuration["Email:Smtp:FromAddress"], out _))
            {
                missing.Add("Email__Smtp__FromAddress");
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
                $"Production yapılandırması güvenli değil: {string.Join(", ", missing)}. " +
                "Eksik/geçersiz sırlar, geçici oturum anahtarları veya demo yüzeyleri veri güvenliği ve " +
                "operasyonel süreklilik riski oluşturur; uygulama bu yüzden başlamayı reddediyor.");
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

    private static bool IsValidBackupEncryptionKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        Span<byte> decoded = stackalloc byte[32];
        return Convert.TryFromBase64String(value, decoded, out var bytesWritten) && bytesWritten == 32;
    }
}
