using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Ops.Domain;
using Abdera.Api.Modules.Ops.Infrastructure;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Progress.Infrastructure;

// Ses Defteri'nin yer tutmasını sınırlayan gece bakımı (issue #52). Üç iş, bu sırayla:
//   1. Budama: retention süresinden eski, işaretsiz ara kayıtlar silinir (AudioClipRetention).
//   2. Yetim dosya: diskte olup veritabanında karşılığı olmayan dosyalar silinir (silme sonrası
//      yarıda kalan dosya, yazılıp satırı kaydedilemeyen yükleme, öğrenci silme).
//   3. Artımlı yedek: henüz yedeklenmemiş kayıtlar şifrelenip yedek hedefine "audio-clips/"
//      altına yüklenir; uzakta karşılığı kalmamış eski yedekler temizlenir.
// Tam döküm her gece yalnızca veritabanını alır; sesler bir kez yüklenir, her gece değil.
public static class AudioClipMaintenance
{
    public const string RemoteDirectory = "audio-clips";
    // Bu yaştan genç dosya yetim sayılmaz: yükleme sürerken (dosya yazıldı, satır henüz yok)
    // dosyayı silmemek için.
    private static readonly TimeSpan OrphanGrace = TimeSpan.FromHours(1);

    public record Result(int Pruned, int OrphansDeleted, int BackedUp, int RemoteDeleted);

    public static async Task<Result> RunAsync(
        AbderaDbContext db, AudioClipStore store, IBackupStorage? backupStorage, IConfiguration config, IClock clock,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var pruned = await PruneAsync(db, store, config, now, cancellationToken);
        var orphans = await DeleteOrphanFilesAsync(db, store, now, cancellationToken);
        var (backedUp, remoteDeleted) = backupStorage is null
            ? (0, 0)
            : await BackupAsync(db, store, backupStorage, config, clock, cancellationToken);
        return new Result(pruned, orphans, backedUp, remoteDeleted);
    }

    private static async Task<int> PruneAsync(
        AbderaDbContext db, AudioClipStore store, IConfiguration config, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var retentionMonths = Math.Max(1, config.GetValue("AudioClips:RetentionMonths", 12));
        var cutoff = now.AddMonths(-retentionMonths);
        // Yalnızca eski kaydı olan öğrenciler: çoğu gece hiç satır okunmaz.
        var studentIds = await db.AudioClips.Where(clip => clip.CreatedAt < cutoff && !clip.Starred)
            .Select(clip => clip.StudentId).Distinct().ToListAsync(cancellationToken);
        var prunable = new List<AudioClip>();
        foreach (var studentId in studentIds)
        {
            var clips = await db.AudioClips.Where(clip => clip.StudentId == studentId).ToListAsync(cancellationToken);
            prunable.AddRange(AudioClipRetention.SelectPrunable(clips, now, retentionMonths));
        }
        if (prunable.Count == 0) return 0;

        db.AudioClips.RemoveRange(prunable);
        db.AuditLogs.Add(AuditLog.Record(
            null, "audio_clip.pruned", nameof(AudioClip), Guid.Empty, now,
            afterJson: JsonSerializer.Serialize(new
            {
                count = prunable.Count,
                bytes = prunable.Sum(clip => (long)clip.SizeBytes),
                retentionMonths,
            })));
        await db.SaveChangesAsync(cancellationToken);
        store.DeleteMany(prunable.Select(clip => clip.FileName));
        return prunable.Count;
    }

    private static async Task<int> DeleteOrphanFilesAsync(
        AbderaDbContext db, AudioClipStore store, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var candidates = store.ListFiles()
            .Where(file => now - new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) > OrphanGrace)
            .ToList();
        if (candidates.Count == 0) return 0;

        var known = (await db.AudioClips.Select(clip => clip.FileName).ToListAsync(cancellationToken)).ToHashSet();
        var orphans = candidates.Where(file => !known.Contains(file.Name)).ToList();
        foreach (var orphan in orphans) orphan.Delete();
        return orphans.Count;
    }

    private static async Task<(int BackedUp, int RemoteDeleted)> BackupAsync(
        AbderaDbContext db, AudioClipStore store, IBackupStorage backupStorage, IConfiguration config, IClock clock,
        CancellationToken cancellationToken)
    {
        // Anahtarsız ortamda (yerel geliştirme) yedek adımı atlanır; ses ASLA şifresiz yüklenmez.
        // Production'da anahtar zorunlu (ProductionSecretsGuard), orada bu dal hiç çalışmaz.
        var key = config["Backup:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(key)) return (0, 0);

        var pending = await db.AudioClips.Where(clip => clip.BackedUpAt == null)
            .OrderBy(clip => clip.CreatedAt).Take(500).ToListAsync(cancellationToken);
        var tempDir = Path.Combine(Path.GetTempPath(), "abdera-audio-backup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var backedUp = 0;
        try
        {
            foreach (var clip in pending)
            {
                var source = store.ExistingPath(clip.FileName);
                if (source is null) continue; // satır var dosya yok: yedeklenecek bir şey yok
                var encrypted = Path.Combine(tempDir, clip.FileName + ".enc");
                await BackupEncryption.EncryptFileAsync(source, encrypted, key, cancellationToken);
                await backupStorage.UploadAsync(encrypted, $"{RemoteDirectory}/{clip.FileName}.enc", cancellationToken);
                File.Delete(encrypted);
                clip.MarkBackedUp(clock.UtcNow);
                backedUp++;
            }
        }
        finally
        {
            // Yüklenenler kaydedilir; yarıda kesilen tur bir sonraki gece kaldığı yerden devam eder.
            await db.SaveChangesAsync(CancellationToken.None);
            Directory.Delete(tempDir, recursive: true);
        }

        // Silinmiş kaydın uzak kopyası veritabanı yedeklerinin tutulma süresi kadar kalır: o
        // süredeki bir döküm geri yüklenirse sesleri de bulunabilsin.
        var retentionDays = config.GetValue("Backup:RetentionDays", 30);
        var cutoff = clock.UtcNow.AddDays(-retentionDays);
        var remote = await backupStorage.ListAsync(RemoteDirectory, cancellationToken);
        var known = (await db.AudioClips.Select(clip => clip.FileName).ToListAsync(cancellationToken)).ToHashSet();
        var remoteDeleted = 0;
        foreach (var file in remote.Where(file => file.ModifiedAt < cutoff))
        {
            var clipFile = Path.GetFileName(file.Name);
            if (clipFile.EndsWith(".enc", StringComparison.Ordinal) && known.Contains(clipFile[..^4])) continue;
            await backupStorage.DeleteAsync(file.Name, cancellationToken);
            remoteDeleted++;
        }

        return (backedUp, remoteDeleted);
    }
}

public class AudioClipMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<AudioClipMaintenanceWorker> logger,
    JobHeartbeatRecorder heartbeat) : BackgroundService
{
    // LessonNoteReminderWorker gibi açılışta değil bir aralık sonra çalışır: paylaşılan test
    // host'larında testlerin kendi çağrısıyla yarışmasın.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(BackgroundJobs.AudioClipMaintenance.Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AbderaDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<AudioClipStore>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var backupDisabled = string.Equals(
                config["Backup:Provider"], BackupProviderModes.Disabled, StringComparison.OrdinalIgnoreCase);
            var backupStorage = backupDisabled ? null : scope.ServiceProvider.GetRequiredService<IBackupStorage>();

            var result = await AudioClipMaintenance.RunAsync(db, store, backupStorage, config, clock, cancellationToken);
            if (result != new AudioClipMaintenance.Result(0, 0, 0, 0))
                logger.LogInformation(
                    "Ses Defteri bakımı: {Pruned} kayıt budandı, {Orphans} yetim dosya silindi, {BackedUp} kayıt yedeklendi, {RemoteDeleted} eski yedek silindi.",
                    result.Pruned, result.OrphansDeleted, result.BackedUp, result.RemoteDeleted);

            await heartbeat.SucceededAsync(BackgroundJobs.AudioClipMaintenance, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ses Defteri bakımı başarısız oldu.");
            await heartbeat.FailedAsync(BackgroundJobs.AudioClipMaintenance, ex, cancellationToken);
        }
    }
}
