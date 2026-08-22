using Abdera.Api.Modules.Ops.Domain;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace Abdera.Api.Modules.Ops.Infrastructure;

public class SftpBackupStorageOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "";
    // İkisinden yalnızca biri dolu olmalı - anahtar tercih edilir (kullanıcı "kendi
    // sunucumuza SSH/SFTP ile" dedi, çoğu sunucuda parola girişi zaten kapalıdır).
    public string? Password { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? PrivateKeyPassphrase { get; set; }
    public string RemoteDirectory { get; set; } = "/backups/abdera";
}

// Backup__Provider=Sftp - kullanıcının kendi sunucusuna SSH anahtarıyla (veya parolayla)
// bağlanır. SSH.NET (Renci.SshNet) kullanıldı: CLI'daki sftp/scp'yi Process.Start ile
// çağırmak yerine (host-key doğrulama, hata kodları, izin sorunları CLI tarafında daha
// kırılgan) - yedekleme veri güvenliği açısından kritik olduğundan (kullanıcının açık
// talebi) test edilebilir, olgun bir .NET kütüphanesi tercih edildi. docs/10-decisions.md G.
public class SftpBackupStorage : IBackupStorage
{
    private readonly SftpBackupStorageOptions _options;
    private readonly ILogger<SftpBackupStorage> _logger;

    public SftpBackupStorage(IOptions<SftpBackupStorageOptions> options, ILogger<SftpBackupStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private SftpClient CreateClient()
    {
        Renci.SshNet.ConnectionInfo connectionInfo;
        if (!string.IsNullOrEmpty(_options.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrEmpty(_options.PrivateKeyPassphrase)
                ? new PrivateKeyFile(_options.PrivateKeyPath)
                : new PrivateKeyFile(_options.PrivateKeyPath, _options.PrivateKeyPassphrase);
            connectionInfo = new Renci.SshNet.ConnectionInfo(_options.Host, _options.Port, _options.Username,
                new PrivateKeyAuthenticationMethod(_options.Username, keyFile));
        }
        else if (!string.IsNullOrEmpty(_options.Password))
        {
            connectionInfo = new Renci.SshNet.ConnectionInfo(_options.Host, _options.Port, _options.Username,
                new PasswordAuthenticationMethod(_options.Username, _options.Password));
        }
        else
        {
            throw new InvalidOperationException(
                "Backup__SftpPrivateKeyPath veya Backup__SftpPassword tanımlı değil - SFTP sunucusuna bağlanacak bir kimlik bilgisi yok.");
        }

        return new SftpClient(connectionInfo);
    }

    public Task UploadAsync(string localFilePath, string remoteFileName, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        client.Connect();
        try
        {
            EnsureRemoteDirectory(client, _options.RemoteDirectory);
            using var fileStream = File.OpenRead(localFilePath);
            var remotePath = $"{_options.RemoteDirectory.TrimEnd('/')}/{remoteFileName}";
            client.UploadFile(fileStream, remotePath);
            _logger.LogInformation("[SftpBackupStorage] yüklendi -> {Path}", remotePath);
        }
        finally
        {
            client.Disconnect();
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RemoteBackupFile>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        client.Connect();
        try
        {
            if (!client.Exists(_options.RemoteDirectory))
            {
                return Task.FromResult<IReadOnlyList<RemoteBackupFile>>([]);
            }

            var files = client.ListDirectory(_options.RemoteDirectory)
                .Where(f => f.IsRegularFile)
                .Select(f => new RemoteBackupFile(f.Name, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero), f.Length))
                .ToList();
            return Task.FromResult<IReadOnlyList<RemoteBackupFile>>(files);
        }
        finally
        {
            client.Disconnect();
        }
    }

    public Task DeleteAsync(string remoteFileName, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        client.Connect();
        try
        {
            var remotePath = $"{_options.RemoteDirectory.TrimEnd('/')}/{remoteFileName}";
            if (client.Exists(remotePath))
            {
                client.DeleteFile(remotePath);
                _logger.LogInformation("[SftpBackupStorage] silindi -> {Path}", remotePath);
            }
        }
        finally
        {
            client.Disconnect();
        }
        return Task.CompletedTask;
    }

    private static void EnsureRemoteDirectory(SftpClient client, string path)
    {
        if (client.Exists(path)) return;

        // "/a/b/c" gibi iç içe bir yol ilk seferde hiç yoksa parça parça oluşturulur.
        var parts = path.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        foreach (var part in parts)
        {
            current += "/" + part;
            if (!client.Exists(current))
            {
                client.CreateDirectory(current);
            }
        }
    }
}
