using Abdera.Api.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Abdera.Tests.Integration;

// docs/09-testing.md: Testcontainers yalnizca gercek Postgres davranisi gerektiren
// entegrasyon testlerinde kullanilir (migration, SKIP LOCKED, unique kisit, idempotency).
//
// Tum test kosusu TEK bir Postgres container'i paylasir: migration'lar bir kez bos bir
// sablon veritabanina uygulanir (MigrationTests'in "bos veritabaninda bastan sona" garantisi
// tam olarak bu adimdir), her fabrika (= her test sinifi) ondan `CREATE DATABASE ... TEMPLATE`
// ile kendi kopyasini acar. Siniflar arasi izolasyon eskisi gibi veritabani seviyesinde;
// degisen yalnizca sinif basina container baslatip tum migration'lari yeniden kosmamak.
public class AbderaWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string TemplateDatabase = "abdera_template";

    private static readonly Lazy<Task<PostgreSqlContainer>> SharedPostgres = new(StartSharedPostgresAsync);
    // CREATE DATABASE ... TEMPLATE, sablona baska bir oturum bagliyken reddedilir; kopyalar
    // sirayla acilir (her biri milisaniyeler surer).
    private static readonly SemaphoreSlim CloneLock = new(1, 1);

    private string _connectionString = "";
    private string _databaseName = "";

    // Ses Defteri dosyaları (AudioClipStore) her fabrikanın kendi geçici klasörüne yazılır.
    public string AudioDirectory { get; } = Path.Combine(Path.GetTempPath(), "abdera-audio-tests", Guid.NewGuid().ToString("N"));

    public async Task InitializeAsync()
    {
        var postgres = await SharedPostgres.Value;
        _databaseName = $"abdera_{Guid.NewGuid():N}";

        await CloneLock.WaitAsync();
        try
        {
            await ExecuteOnServerAsync(postgres, $"CREATE DATABASE \"{_databaseName}\" TEMPLATE \"{TemplateDatabase}\"");
        }
        finally
        {
            CloneLock.Release();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Database = _databaseName,
        }.ConnectionString;
    }

    public new async Task DisposeAsync()
    {
        // Once host (arka plan servisleri dahil) kapanir, sonra sinifin veritabani silinir.
        // Container'in kendisi kosu bitince Testcontainers'in Ryuk temizleyicisiyle gider.
        await base.DisposeAsync();
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            NpgsqlConnection.ClearPool(connection);
        }
        var postgres = await SharedPostgres.Value;
        await ExecuteOnServerAsync(postgres, $"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
        if (Directory.Exists(AudioDirectory)) Directory.Delete(AudioDirectory, recursive: true);
    }

    private static async Task<PostgreSqlContainer> StartSharedPostgresAsync()
    {
        var postgres = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("abdera_test")
            .WithUsername("abdera_test")
            .WithPassword("abdera_test")
            // Eskiden her sinifin kendi sunucusu (100 baglanti) vardi; simdi ayni anda kosan
            // tum siniflarin host'lari + arka plan servisleri tek sunucuyu paylasiyor.
            .WithCommand("-c", "max_connections=500")
            .Build();
        await postgres.StartAsync();

        await ExecuteOnServerAsync(postgres, $"CREATE DATABASE \"{TemplateDatabase}\"");
        var templateConnection = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Database = TemplateDatabase,
            Pooling = false,
        }.ConnectionString;
        await using (var db = new AbderaDbContext(new DbContextOptionsBuilder<AbderaDbContext>()
            .UseNpgsql(templateConnection).Options))
        {
            await db.Database.MigrateAsync();
            // Migration canlı okul için dönem (4 ders) aidatını açar. Testler gerçek saatle koşuyor:
            // açık kalsaydı geçiş tarihinden sonra birebir kayıtların aylık aidat testleri kendiliğinden
            // kırılırdı. Varsayılan kapalı; dönem testleri Fiyat politikasından kendileri açar.
            await db.Database.ExecuteSqlRawAsync("UPDATE billing_settings SET cycle_billing_starts_on = NULL");
        }

        return postgres;
    }

    private static async Task ExecuteOnServerAsync(PostgreSqlContainer postgres, string sql)
    {
        // Pooling=false: havuzda acik kalan bir baglanti sablonu "baska kullanici bagli"
        // durumuna dusurup sonraki kopyayi reddettirmesin.
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            Database = "postgres",
            Pooling = false,
        }.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                ["WhatsApp:Provider"] = "Fake",
                // SEC-1/SEC-2: WebhookSignatureVerifier/RsvpButtonPayload artik bos anahtarda
                // fail-closed olduklarindan testlerin gercekci (bos olmayan) bir test secret'i
                // olmasi lazim - bkz. MessagingFlowTests.
                ["WhatsApp:AppSecret"] = "test-webhook-app-secret",
                ["WhatsApp:PayloadSigningKey"] = "test-payload-signing-key",
                ["WhatsApp:WebhookVerifyToken"] = "test-webhook-verify-token",
                ["Banking:WebhookSharedSecret"] = "test-bank-webhook-secret",
                ["Bootstrap:AdminEmail"] = "admin@test.local",
                ["Bootstrap:AdminPassword"] = "Test1234!",
                // NotificationDispatcher testlerinin gerçek 60 saniye beklemesine gerek kalmasın diye.
                ["Notifications:DispatchIntervalSeconds"] = "1",
                // SEC-3: paylaşılan bu factory'yi kullanan test sınıflarının çoğu
                // CreateAdminClientAsync üzerinden onlarca kez giriş yapıyor (bkz.
                // MessagingFlowTests). Varsayılan 5/15dk limiti burada pratikte devre dışı
                // bırakılıyor - RateLimitingFlowTests kendi düşük limitli factory'sini
                // WithWebHostBuilder ile kurup asıl davranışı doğruluyor.
                ["RateLimiting:LoginPermitLimit"] = "10000",
                ["RateLimiting:GuardianOtpPermitLimit"] = "10000",
                ["RateLimiting:AssistantPermitLimit"] = "10000",
                // Faz 4: BackupService.RunPgDumpAsync sonrası gerçekten şifreleme çalışsın diye -
                // testlerde Backup:Provider/Email:Provider ayarlanmadığı için ikisi de varsayılan
                // Fake kalır (OpsFlowTests), yalnızca şifreleme anahtarı gerçek olmalı.
                ["AudioClips:Directory"] = AudioDirectory,
                ["Backup:EncryptionKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            });
        });
    }

    public async Task<AbderaDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<AbderaDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        var context = new AbderaDbContext(options);
        await context.Database.MigrateAsync();
        return context;
    }
}
