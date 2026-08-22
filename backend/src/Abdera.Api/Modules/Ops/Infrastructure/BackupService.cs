using System.Diagnostics;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Shared;
using Npgsql;

namespace Abdera.Api.Modules.Ops.Infrastructure;

// Faz 4 (docs/15-product-phases.md): günlük şifreli yedekleme. CLAUDE.md'nin Spring
// Scheduler karşılığı: BackgroundService + PeriodicTimer, Hangfire/Quartz yok (aynı desen
// NotificationDispatcher/OverdueReceivableSweeper'da). OS cron'a bağımlı olmamak için
// belirli bir saat yerine "bugün henüz çalışmadıysa ve o saat geçtiyse çalıştır" mantığıyla
// periyodik kontrol edilir - konteyner yeniden başlasa bile kaçırılmış bir gün fark edilir.
public class BackupService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<BackupService> logger) : BackgroundService
{
    // Program.cs'te AddSingleton ile kaydedilir - Trigger() elle tetikleme uç noktasından
    // (Features/BackupRuns.cs) çağrılabilsin diye durumu burada tutulur.
    public DateOnly? LastRunDate { get; private set; }
    private readonly SemaphoreSlim _runLock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var checkIntervalMinutes = config.GetValue("Backup:CheckIntervalMinutes", 15);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(checkIntervalMinutes));
        do
        {
            await RunIfDueAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunIfDueAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var now = clock.UtcNow;
        var local = clock.ToSchoolLocal(now);
        var today = DateOnly.FromDateTime(local.Date);

        if (LastRunDate == today) return;

        var runTimeLocal = TimeOnly.Parse(config["Backup:DailyRunTimeLocal"] ?? "03:00");
        if (TimeOnly.FromDateTime(local.DateTime) < runTimeLocal) return;

        await RunOnceAsync(triggeredManually: false, cancellationToken);
    }

    // POST /api/backup-runs/trigger tarafından çağrılır - manuel tetikleme günün otomatik
    // koşusunu da "bugün yapıldı" sayar (aynı gün içinde iki kez pg_dump almaya gerek yok).
    public async Task TriggerManualRunAsync(CancellationToken cancellationToken = default) =>
        await RunOnceAsync(triggeredManually: true, cancellationToken);

    private async Task RunOnceAsync(bool triggeredManually, CancellationToken cancellationToken)
    {
        if (!await _runLock.WaitAsync(0, cancellationToken))
        {
            logger.LogInformation("Bir yedekleme zaten sürüyor, bu tetikleme atlandı.");
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var storage = scope.ServiceProvider.GetRequiredService<IBackupStorage>();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

            var now = clock.UtcNow;
            var run = BackupRun.Start(triggeredManually, now);
            db.BackupRuns.Add(run);
            await db.SaveChangesAsync(cancellationToken);

            var localDate = clock.ToSchoolLocal(now);
            LastRunDate = DateOnly.FromDateTime(localDate.Date);

            var tempDir = Path.Combine(Path.GetTempPath(), "abdera-backups");
            Directory.CreateDirectory(tempDir);
            var dumpFileName = $"abdera-{localDate:yyyyMMdd-HHmmss}.sql";
            var dumpPath = Path.Combine(tempDir, dumpFileName);
            var encryptedFileName = $"{dumpFileName}.enc";
            var encryptedPath = Path.Combine(tempDir, encryptedFileName);

            try
            {
                var scopedConfig = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                await RunPgDumpAsync(scopedConfig, dumpPath, cancellationToken);

                var encryptionKey = config["Backup:EncryptionKey"]
                    ?? throw new InvalidOperationException("Backup__EncryptionKey tanımlı değil - `openssl rand -base64 32` ile üretip .env'e ekle.");
                await BackupEncryption.EncryptFileAsync(dumpPath, encryptedPath, encryptionKey, cancellationToken);

                var sizeBytes = new FileInfo(encryptedPath).Length;
                await storage.UploadAsync(encryptedPath, encryptedFileName, cancellationToken);
                await EnforceRetentionAsync(storage, config, cancellationToken);

                run.MarkSucceeded(encryptedFileName, sizeBytes, clock.UtcNow);
                logger.LogInformation("Yedekleme tamamlandı: {File} ({Size} byte)", encryptedFileName, sizeBytes);
            }
            catch (Exception ex)
            {
                run.MarkFailed(ex.Message, clock.UtcNow);
                logger.LogError(ex, "Yedekleme başarısız oldu.");

                // docs/15-product-phases.md Faz 4 kabul kriteri: "yedek alınamazsa ...
                // audit olayı ... üretilecek." ActorUserId=null - guardian.opted_out'taki
                // gibi sistem-kaynaklı olay, bir admin yok.
                db.AuditLogs.Add(Abdera.Api.Modules.Auth.Domain.AuditLog.Record(
                    null, "backup.failed", nameof(BackupRun), run.Id, clock.UtcNow,
                    afterJson: System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message, triggeredManually })));

                // ARC-2 desenindeki gibi - beklenmedik bir e-posta hatası yedekleme
                // kaydının işlenmesini bozmasın diye ayrı bir try/catch içinde.
                try
                {
                    var recipients = ParseRecipients(config);
                    if (recipients.Count > 0)
                    {
                        await emailSender.SendAsync(recipients, "Abdera - Yedekleme başarısız oldu",
                            $"Günlük veritabanı yedeklemesi başarısız oldu.\n\nHata: {ex.Message}\n\nZaman: {clock.ToSchoolLocal(clock.UtcNow):dd.MM.yyyy HH:mm}",
                            cancellationToken);
                    }
                }
                catch (Exception emailEx)
                {
                    logger.LogError(emailEx, "Yedekleme hatası e-postası gönderilemedi.");
                }
            }
            finally
            {
                File.Delete(dumpPath);
                File.Delete(encryptedPath);
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private static async Task RunPgDumpAsync(IConfiguration config, string outputPath, CancellationToken cancellationToken)
    {
        // ConnectionStrings:Default zaten Npgsql biçiminde - ayrı POSTGRES_* değişkenleri
        // API konteynerine geçirilmiyor (docker-compose.yml), bu yüzden pg_dump'ın
        // ihtiyaç duyduğu host/port/kullanıcı/parola buradan ayrıştırılır. IConfiguration
        // üzerinden okunur (Program.cs'teki ResolveConnectionString ile aynı desen) - ham
        // process ortam değişkeni okumak WebApplicationFactory'nin test-time in-memory
        // config override'ını (Testcontainers bağlantı dizesi) görmezden gelirdi.
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default tanımlı değil.");
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        var startInfo = new ProcessStartInfo
        {
            FileName = "pg_dump",
            ArgumentList =
            {
                "--host", builder.Host ?? "localhost",
                "--port", builder.Port.ToString(),
                "--username", builder.Username ?? "",
                "--dbname", builder.Database ?? "",
                "--no-password",
                "--format", "plain",
                "--file", outputPath,
            },
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.Environment["PGPASSWORD"] = builder.Password ?? "";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pg_dump süreci başlatılamadı.");
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"pg_dump {process.ExitCode} koduyla başarısız oldu: {stderr}");
        }
    }

    private static async Task EnforceRetentionAsync(IBackupStorage storage, IConfiguration config, CancellationToken cancellationToken)
    {
        var retentionDays = config.GetValue("Backup:RetentionDays", 30);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        var files = await storage.ListAsync(cancellationToken);
        foreach (var file in files.Where(f => f.ModifiedAt < cutoff))
        {
            await storage.DeleteAsync(file.Name, cancellationToken);
        }
    }

    internal static List<string> ParseRecipients(IConfiguration config) =>
        (config["Ops:AlertRecipients"] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
}
