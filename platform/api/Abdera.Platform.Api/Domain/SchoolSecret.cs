namespace Abdera.Platform.Api.Domain;

// Okulun sırları (§3.2 school_secrets): veritabanında yalnızca Data Protection ile şifreli hali durur
// (SecretVault). API cevaplarında hiçbir zaman dönmez; tek istisna yöneticinin geçici parolasını
// açıkça "göster" demesi, o da audit'e yazılır.
public class SchoolSecret
{
    public Guid SchoolId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Ciphertext { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private SchoolSecret() { }

    public static SchoolSecret Create(Guid schoolId, string name, string ciphertext, DateTimeOffset now) => new()
    {
        SchoolId = schoolId,
        Name = name,
        Ciphertext = ciphertext,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void Replace(string ciphertext, DateTimeOffset now) { Ciphertext = ciphertext; UpdatedAt = now; }
}

// Sır adları tek yerde; .env anahtarına eşleşenler SchoolEnvBuilder'da.
public static class SecretNames
{
    public const string PostgresPassword = "postgres_password";
    public const string BackupEncryptionKey = "backup_encryption_key";
    public const string BackupGatewayPassword = "backup_gw_password";
    public const string ProvisioningToken = "provisioning_token";
    public const string TelemetryToken = "telemetry_token";
    public const string AlertsSecret = "alerts_secret";
    public const string WhatsAppPayloadSigningKey = "whatsapp_payload_signing_key";
    public const string AdminTemporaryPassword = "admin_temporary_password";
    // Sihirbazdan gelen kurulum isteği (yönetici, profil, kurallar, tarifeler); geçici parola içerir.
    public const string ProvisionRequest = "provision_request";
    // Sihirbazda girilen entegrasyon sırları: "integration:WhatsApp__AccessToken" gibi .env anahtarıyla.
    public const string IntegrationPrefix = "integration:";
    // Sahiplenmede sunucuya konan döküm dosyasının adı (sır değil ama okulla birlikte saklanır).
    public const string AdoptDumpName = "adopt_dump_name";
}
