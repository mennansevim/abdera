using Abdera.Api.Modules.Ops.Domain;

namespace Abdera.Api.Modules.Ops.Infrastructure;

// Production'da harici yedekleme henüz kurulmadığında Fake storage gibi başarı taklidi
// yapmaz. Savunma amaçlı olarak tüm işlemleri açık bir hatayla reddeder.
public class DisabledBackupStorage : IBackupStorage
{
    private static InvalidOperationException DisabledException() =>
        new("Harici yedekleme entegrasyonu şu anda devre dışı.");

    public Task UploadAsync(string localFilePath, string remoteFileName, CancellationToken cancellationToken = default) =>
        Task.FromException(DisabledException());

    public Task<IReadOnlyList<RemoteBackupFile>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyList<RemoteBackupFile>>(DisabledException());

    public Task DeleteAsync(string remoteFileName, CancellationToken cancellationToken = default) =>
        Task.FromException(DisabledException());
}
