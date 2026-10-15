using System.Security.Cryptography;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Api.Infrastructure;

// Okul sırlarının şifrelenmesi (§3.2, §11). Anahtarlar Platform:KeysDirectory'de; kaybolursa tüm
// okulların sırları okunamaz - anahtar dizini ayrıca yedeklenmeli (docs/multi-tenant/PLATFORM.md).
public class SecretVault(IDataProtectionProvider provider, PlatformDbContext db, TimeProvider time)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Abdera.Platform.SchoolSecrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);

    public async Task SetAsync(Guid schoolId, string name, string value)
    {
        var now = time.GetUtcNow();
        var existing = await db.SchoolSecrets.SingleOrDefaultAsync(s => s.SchoolId == schoolId && s.Name == name);
        if (existing is null) db.SchoolSecrets.Add(SchoolSecret.Create(schoolId, name, Protect(value), now));
        else existing.Replace(Protect(value), now);
    }

    // Yalnızca yoksa üretir: kurulum adımı yeniden çalışırsa sır değişmez (idempotent).
    public async Task<string> GetOrCreateAsync(Guid schoolId, string name, Func<string> generate)
    {
        var existing = await db.SchoolSecrets.AsNoTracking().SingleOrDefaultAsync(s => s.SchoolId == schoolId && s.Name == name);
        if (existing is not null) return Unprotect(existing.Ciphertext);
        var value = generate();
        db.SchoolSecrets.Add(SchoolSecret.Create(schoolId, name, Protect(value), time.GetUtcNow()));
        return value;
    }

    public async Task<string?> GetAsync(Guid schoolId, string name)
    {
        var existing = await db.SchoolSecrets.AsNoTracking().SingleOrDefaultAsync(s => s.SchoolId == schoolId && s.Name == name);
        return existing is null ? null : Unprotect(existing.Ciphertext);
    }

    public async Task<Dictionary<string, string>> GetAllAsync(Guid schoolId) =>
        (await db.SchoolSecrets.AsNoTracking().Where(s => s.SchoolId == schoolId).ToListAsync())
            .ToDictionary(s => s.Name, s => Unprotect(s.Ciphertext));

    public static string RandomToken(int bytes = 32) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string RandomKeyBase64(int bytes = 32) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));

    // Okul yöneticisine verilecek geçici parola: okunaklı (karışan harf yok), 16 karakter.
    public static string TemporaryPassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
        var chars = new char[16];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(chars);
    }
}
