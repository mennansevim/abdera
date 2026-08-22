using Abdera.Api.Modules.Ops.Domain;

namespace Abdera.Api.Modules.Ops.Infrastructure;

// Backup__Provider=Fake (dev/test varsayılanı). Gerçek bir SFTP sunucusuna bağlanmaz;
// yüklemeyi loglar ve bellek-içi bir liste tutar ki BackupService akışı gerçek bir sunucu
// olmadan uçtan uca izlenebilsin (WhatsApp/Banking'teki Fake istemcilerle aynı desen).
public class FakeBackupStorage(ILogger<FakeBackupStorage> logger) : IBackupStorage
{
    private readonly List<RemoteBackupFile> _files = [];

    public Task UploadAsync(string localFilePath, string remoteFileName, CancellationToken cancellationToken = default)
    {
        var size = new FileInfo(localFilePath).Length;
        _files.Add(new RemoteBackupFile(remoteFileName, DateTimeOffset.UtcNow, size));
        logger.LogInformation("[FakeBackupStorage] yüklendi -> {File} ({Size} byte)", remoteFileName, size);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RemoteBackupFile>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RemoteBackupFile>>(_files.ToList());

    public Task DeleteAsync(string remoteFileName, CancellationToken cancellationToken = default)
    {
        _files.RemoveAll(f => f.Name == remoteFileName);
        logger.LogInformation("[FakeBackupStorage] silindi -> {File}", remoteFileName);
        return Task.CompletedTask;
    }
}
