namespace Abdera.Api.Modules.Ops.Domain;

// docs/06-whatsapp.md/docs/12-bank-integration.md'deki Fake/gerçek sağlayıcı ikilisiyle aynı
// desen (abdera-notification skill kuralı): FakeBackupStorage (dev/test varsayılanı) +
// SftpBackupStorage (gerçek, kullanıcının kendi sunucusuna SSH/SFTP). Provider seçimi
// Backup__Provider ortam değişkeninden gelir, kod içinde hardcode edilmez (CLAUDE.md).
public interface IBackupStorage
{
    Task UploadAsync(string localFilePath, string remoteFileName, CancellationToken cancellationToken = default);
    // subdirectory: Ses Defteri yedekleri "audio-clips/" altında durur (AudioClipMaintenance).
    // Verilmezse yalnızca kök klasördeki dosyalar döner; dönen Name, Upload/Delete'e verilecek
    // uzak addır ("audio-clips/<dosya>").
    Task<IReadOnlyList<RemoteBackupFile>> ListAsync(string? subdirectory = null, CancellationToken cancellationToken = default);
    Task DeleteAsync(string remoteFileName, CancellationToken cancellationToken = default);
}

public record RemoteBackupFile(string Name, DateTimeOffset ModifiedAt, long SizeBytes);
