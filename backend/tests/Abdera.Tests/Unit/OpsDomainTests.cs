using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Infrastructure;

namespace Abdera.Tests.Unit;

// Faz 4 (docs/15-product-phases.md): yedekleme şifreleme round-trip'i ve sistem sağlığı
// karar mantığı - ikisi de DB/dış servise dokunmayan saf fonksiyonlar, PaymentMatcher.Match
// ile aynı desen (docs/09-testing.md).
public class OpsDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task BackupEncryption_round_trip_recovers_original_content()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "abdera-backup-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "dump.sql");
            var encryptedPath = Path.Combine(tempDir, "dump.sql.enc");
            var decryptedPath = Path.Combine(tempDir, "dump.decrypted.sql");
            var originalContent = "CREATE TABLE ornek (id uuid);\nINSERT INTO ornek VALUES ('" + Guid.NewGuid() + "');";
            await File.WriteAllTextAsync(sourcePath, originalContent);

            var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            await BackupEncryption.EncryptFileAsync(sourcePath, encryptedPath, key);
            await BackupEncryption.DecryptFileAsync(encryptedPath, decryptedPath, key);

            var decryptedContent = await File.ReadAllTextAsync(decryptedPath);
            Assert.Equal(originalContent, decryptedContent);

            // Şifreli içerik düz metinle aynı olmamalı - gerçekten şifrelendiğini doğrular.
            var encryptedBytes = await File.ReadAllBytesAsync(encryptedPath);
            Assert.DoesNotContain(originalContent, System.Text.Encoding.UTF8.GetString(encryptedBytes), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task BackupEncryption_decrypt_fails_with_wrong_key()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "abdera-backup-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePath = Path.Combine(tempDir, "dump.sql");
            var encryptedPath = Path.Combine(tempDir, "dump.sql.enc");
            await File.WriteAllTextAsync(sourcePath, "gizli veri");

            var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var wrongKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            await BackupEncryption.EncryptFileAsync(sourcePath, encryptedPath, key);

            await Assert.ThrowsAsync<System.Security.Cryptography.AuthenticationTagMismatchException>(
                () => BackupEncryption.DecryptFileAsync(encryptedPath, Path.Combine(tempDir, "out.sql"), wrongKey));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SystemHealthMonitor_Evaluate_reports_unhealthy_when_db_is_down()
    {
        var (level, _) = SystemHealthMonitor.Evaluate(dbHealthy: false, latestBackup: null, lastSuccessAge: null, staleAfterHours: 30, unhealthyAfterHours: 48);

        Assert.Equal(SystemHealthLevel.Unhealthy, level);
    }

    [Fact]
    public void SystemHealthMonitor_Evaluate_reports_degraded_when_no_backup_has_ever_run()
    {
        var (level, _) = SystemHealthMonitor.Evaluate(dbHealthy: true, latestBackup: null, lastSuccessAge: null, staleAfterHours: 30, unhealthyAfterHours: 48);

        Assert.Equal(SystemHealthLevel.Degraded, level);
    }

    [Fact]
    public void SystemHealthMonitor_Evaluate_reports_unhealthy_when_last_backup_failed_and_none_ever_succeeded()
    {
        var failed = BackupRun.Start(triggeredManually: false, Now);
        failed.MarkFailed("pg_dump başarısız", Now);

        var (level, detail) = SystemHealthMonitor.Evaluate(dbHealthy: true, latestBackup: failed, lastSuccessAge: null, staleAfterHours: 30, unhealthyAfterHours: 48);

        Assert.Equal(SystemHealthLevel.Unhealthy, level);
        Assert.Contains("pg_dump başarısız", detail);
    }

    [Theory]
    [InlineData(10, SystemHealthLevel.Healthy)]
    [InlineData(31, SystemHealthLevel.Degraded)]
    [InlineData(49, SystemHealthLevel.Unhealthy)]
    public void SystemHealthMonitor_Evaluate_escalates_by_backup_age(int hoursSinceSuccess, SystemHealthLevel expected)
    {
        var succeeded = BackupRun.Start(triggeredManually: false, Now);
        succeeded.MarkSucceeded("abdera-x.sql.enc", 1024, Now);

        var (level, _) = SystemHealthMonitor.Evaluate(
            dbHealthy: true, latestBackup: succeeded, lastSuccessAge: TimeSpan.FromHours(hoursSinceSuccess),
            staleAfterHours: 30, unhealthyAfterHours: 48);

        Assert.Equal(expected, level);
    }

    [Fact]
    public void SystemHealthStatus_ShouldSendAlert_respects_cooldown()
    {
        var status = SystemHealthStatus.CreateDefault(Now);
        status.Update(SystemHealthLevel.Unhealthy, "DB down", Now);

        Assert.True(status.ShouldSendAlert(Now, TimeSpan.FromMinutes(60)));

        status.MarkAlertSent(Now);
        Assert.False(status.ShouldSendAlert(Now.AddMinutes(30), TimeSpan.FromMinutes(60)));
        Assert.True(status.ShouldSendAlert(Now.AddMinutes(61), TimeSpan.FromMinutes(60)));
    }

    [Fact]
    public void SystemHealthStatus_ShouldSendAlert_is_false_when_healthy()
    {
        var status = SystemHealthStatus.CreateDefault(Now);
        status.Update(SystemHealthLevel.Healthy, null, Now);

        Assert.False(status.ShouldSendAlert(Now, TimeSpan.FromMinutes(60)));
    }
}
