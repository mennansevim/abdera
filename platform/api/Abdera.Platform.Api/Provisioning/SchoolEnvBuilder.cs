using System.Text;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Infrastructure;

namespace Abdera.Platform.Api.Provisioning;

// Okulun .env dosyası (deploy/instance/env.template ile aynı anahtarlar). Değerler sır kasasından ve
// platform yapılandırmasından gelir; yalnızca ajanın stdin'ine yazılır, diske/loga düşmez.
public class SchoolEnvBuilder(IConfiguration config)
{
    public const string ModuleWhatsApp = "whatsapp";
    public const string ModuleEmail = "email";
    public const string ModuleAi = "ai";

    private static readonly string[] WhatsAppKeys =
        ["WhatsApp__AccessToken", "WhatsApp__AppSecret", "WhatsApp__PhoneNumberId", "WhatsApp__BusinessAccountId", "WhatsApp__WebhookVerifyToken"];
    private static readonly string[] SmtpKeys =
        ["Email__Smtp__Host", "Email__Smtp__Port", "Email__Smtp__Username", "Email__Smtp__Password", "Email__Smtp__FromAddress"];

    // Sihirbazda girilebilecek entegrasyon anahtarları (beyaz liste): başka bir .env anahtarı yazılamaz.
    public static readonly IReadOnlySet<string> AllowedIntegrationKeys =
        new HashSet<string>([.. WhatsAppKeys, .. SmtpKeys, "Ai__ApiKey", "Ops__AlertRecipients"]);

    public string Build(School school, Release release, IReadOnlyDictionary<string, string> secrets)
    {
        string Secret(string name) => secrets.TryGetValue(name, out var value) ? value : throw new InvalidOperationException($"Sır eksik: {name}");
        var integrations = secrets
            .Where(pair => pair.Key.StartsWith(SecretNames.IntegrationPrefix, StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key[SecretNames.IntegrationPrefix.Length..], pair => pair.Value);

        var whatsApp = school.Modules.Contains(ModuleWhatsApp) && WhatsAppKeys.Where(key => key != "WhatsApp__BusinessAccountId").All(integrations.ContainsKey);
        var email = school.Modules.Contains(ModuleEmail) && SmtpKeys.All(integrations.ContainsKey);
        var ai = school.Modules.Contains(ModuleAi) && integrations.ContainsKey("Ai__ApiKey");
        var platformUrl = (config["Platform:PublicUrl"] ?? "https://platform.abdera.app").TrimEnd('/');

        var lines = new List<(string Key, string Value)>
        {
            ("INSTANCE_SLUG", school.Slug),
            ("ABDERA_API_IMAGE", release.ApiImage),
            ("ABDERA_WEB_IMAGE", release.WebImage),
            ("App__Version", release.Version),
            ("POSTGRES_PASSWORD", Secret(SecretNames.PostgresPassword)),
            ("Frontend__Origin", $"https://{school.PrimaryDomain}"),
            ("SCHOOL_TIMEZONE", config["Schools:TimeZone"] ?? "Europe/Istanbul"),
            ("Modules__Enabled", school.ModulesEnvValue),
            ("Provisioning__Token", Secret(SecretNames.ProvisioningToken)),
            ("Telemetry__Token", Secret(SecretNames.TelemetryToken)),
            // Okul uygulaması https olmayan alarm adresiyle açılmaz (ProductionSecretsGuard). Platform https
            // değilse (yalnızca yerel test ortamı; production'da Program.cs https zorunlu tutar) alarm kapalı.
            ("Alerts__WebhookUrl", platformUrl.StartsWith("https://", StringComparison.Ordinal) ? $"{platformUrl}/api/alerts" : ""),
            ("Alerts__WebhookSecret", Secret(SecretNames.AlertsSecret)),
            ("Backup__Provider", "Sftp"),
            ("Backup__EncryptionKey", Secret(SecretNames.BackupEncryptionKey)),
            ("Backup__Sftp__Host", "backup-gw"),
            ("Backup__Sftp__Port", "2022"),
            ("Backup__Sftp__Username", "abdera"),
            ("Backup__Sftp__Password", Secret(SecretNames.BackupGatewayPassword)),
            ("BACKUP_GW_PASSWORD", Secret(SecretNames.BackupGatewayPassword)),
            ("R2_BUCKET", config["Backup:R2:Bucket"] ?? "abdera-backups"),
            ("R2_ACCOUNT_ID", config["Backup:R2:AccountId"] ?? ""),
            // Okul anahtarının silme yetkisi olmamalı (§16.9); platform yapılandırmasında belgelenir.
            ("R2_ACCESS_KEY_ID", config["Backup:R2:AccessKeyId"] ?? ""),
            ("R2_SECRET_ACCESS_KEY", config["Backup:R2:SecretAccessKey"] ?? ""),
            // Yalnızca yerel test ortamı (yerel S3); production'da boş kalır, okul Cloudflare R2'ye gider.
            ("R2_ENDPOINT", config["Backup:R2:Endpoint"] ?? ""),
            ("R2_PROVIDER", config["Backup:R2:Provider"] ?? ""),
            ("WhatsApp__Provider", whatsApp ? "Cloud" : "Disabled"),
            ("WhatsApp__PayloadSigningKey", Secret(SecretNames.WhatsAppPayloadSigningKey)),
            ("Email__Provider", email ? "Smtp" : "Disabled"),
            ("Banking__Provider", "Manual"),
            ("Ai__Provider", ai ? "OpenAi" : "Disabled"),
            ("Auth__CookieName", "abdera_session"),
            ("Auth__DevLogin__Enabled", "false"),
            ("Demo__Enabled", "false"),
            ("Bootstrap__AdminEmail", ""),
        };
        foreach (var (key, value) in integrations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            if (AllowedIntegrationKeys.Contains(key)) lines.Add((key, value));

        var builder = new StringBuilder();
        foreach (var (key, value) in lines)
        {
            // .env satır biçimini bozabilecek değer reddedilir (ajan KEY=VALUE satırı bekler).
            if (value.Contains('\n') || value.Contains('\r') || value.Contains('\0'))
                throw new InvalidOperationException($"{key} değeri satır sonu içeremez.");
            builder.Append(key).Append('=').Append(value).Append('\n');
        }
        return builder.ToString();
    }
}
