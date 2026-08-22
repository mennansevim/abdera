using System.Security.Cryptography;

namespace Abdera.Api.Modules.Ops.Infrastructure;

// "Günlük şifreli yedekleme" (docs/15-product-phases.md Faz 4) - .NET'in yerleşik AES-GCM'i
// kullanılır (System.Security.Cryptography.AesGcm), ek bir kütüphaneye gerek yok (CLAUDE.md
// "gereksiz bağımlılık ekleme"). Dosya biçimi: [12 byte nonce][ciphertext][16 byte tag].
// Anahtar Backup__EncryptionKey'den (base64, 32 byte / AES-256) okunur - üretimde bu değer
// olmadan yedekleme başlamaz (bkz. Program.cs ProductionSecretsGuard genişletmesi).
public static class BackupEncryption
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static async Task EncryptFileAsync(string sourcePath, string destinationPath, string base64Key, CancellationToken cancellationToken = default)
    {
        var key = Convert.FromBase64String(base64Key);
        if (key.Length != 32)
        {
            throw new InvalidOperationException("Backup__EncryptionKey base64 çözüldüğünde 32 byte (AES-256) olmalı - `openssl rand -base64 32` ile üretilebilir.");
        }

        var plaintext = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aesGcm = new AesGcm(key, TagSize))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        await using var output = File.Create(destinationPath);
        await output.WriteAsync(nonce, cancellationToken);
        await output.WriteAsync(ciphertext, cancellationToken);
        await output.WriteAsync(tag, cancellationToken);
    }

    // Geri yükleme provası ve olası manuel kurtarma için - bkz. docs/16-backup-restore.md.
    public static async Task DecryptFileAsync(string sourcePath, string destinationPath, string base64Key, CancellationToken cancellationToken = default)
    {
        var key = Convert.FromBase64String(base64Key);
        var encrypted = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        if (encrypted.Length < NonceSize + TagSize)
        {
            throw new InvalidOperationException("Şifreli yedek dosyası bozuk görünüyor (beklenenden kısa).");
        }

        var nonce = encrypted[..NonceSize];
        var tag = encrypted[^TagSize..];
        var ciphertext = encrypted[NonceSize..^TagSize];
        var plaintext = new byte[ciphertext.Length];

        using (var aesGcm = new AesGcm(key, TagSize))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        await File.WriteAllBytesAsync(destinationPath, plaintext, cancellationToken);
    }
}
