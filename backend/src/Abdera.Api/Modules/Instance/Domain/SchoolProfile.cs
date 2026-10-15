using System.Text.RegularExpressions;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Instance.Domain;

// Okulun kimliği: adı, kısa adı, ana rengi ve logosu (docs/multi-tenant/ARCHITECTURE.md §5.1).
// Tek satır - BillingSettings ile aynı singleton deseni. Satır yoksa bugünkü Abdera kimliği
// döner, yani bu tablo boşken arayüz hiçbir şey değişmemiş gibi görünür.
//
// Logo, ScoreFile/StudentPhoto gibi veritabanında tutulur: ayrı volume yok, okulun yedeğiyle
// birlikte taşınır ve geri yüklenir.
public partial class SchoolProfile
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    public const string DefaultName = "Abdera Sanat Akademi";
    public const string DefaultShortName = "Abdera";
    public const int MaxNameLength = 120;
    public const int MaxShortNameLength = 40;
    public const int MaxLogoBytes = 512 * 1024;
    public const int MaxLegalNameLength = 200;
    public const int MaxContactEmailLength = 254;

    public Guid Id { get; private set; }
    public string Name { get; private set; } = DefaultName;
    public string ShortName { get; private set; } = DefaultShortName;
    public string? PrimaryColor { get; private set; }
    // Gizlilik metnindeki veri sorumlusu unvanı ve KVKK başvuru adresi (herkese açık okul bilgisi).
    // Boşsa bugünkü Abdera metni gösterilir.
    public string? LegalName { get; private set; }
    public string? ContactEmail { get; private set; }
    public byte[]? Logo { get; private set; }
    public string? LogoContentType { get; private set; }
    // Logo değiştikçe yenilenir; ETag ve istemci önbelleğini kırmak için URL'e eklenir.
    public Guid? LogoVersion { get; private set; }
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint RowVersion { get; private set; }

    private SchoolProfile() { }

    public static SchoolProfile CreateDefault(DateTimeOffset now) => new()
    {
        Id = SingletonId,
        CreatedAt = now,
        UpdatedAt = now,
    };

    // Satır yoksa kalıcı olmayan bir varsayılan döner - salt okuma çağrısı Add/Save tetiklemez.
    public static async Task<SchoolProfile> GetCurrentAsync(AbderaDbContext db) =>
        await db.SchoolProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == SingletonId)
        ?? CreateDefault(DateTimeOffset.MinValue);

    public bool HasLogo => Logo is { Length: > 0 };

    public void UpdateIdentity(string name, string shortName, string? primaryColor, Guid? updatedBy, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        name = (name ?? string.Empty).Trim();
        shortName = (shortName ?? string.Empty).Trim();
        primaryColor = string.IsNullOrWhiteSpace(primaryColor) ? null : primaryColor.Trim().ToLowerInvariant();

        if (name.Length is 0 or > MaxNameLength)
            errors["name"] = [$"Okul adı 1-{MaxNameLength} karakter olmalı."];
        if (shortName.Length is 0 or > MaxShortNameLength)
            errors["shortName"] = [$"Kısa ad 1-{MaxShortNameLength} karakter olmalı."];
        if (primaryColor is not null && !HexColor().IsMatch(primaryColor))
            errors["primaryColor"] = ["Renk #RRGGBB biçiminde olmalı (örn. #5b3fa8)."];
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        Name = name;
        ShortName = shortName;
        PrimaryColor = primaryColor;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void UpdateContact(string? legalName, string? contactEmail, Guid? updatedBy, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        legalName = string.IsNullOrWhiteSpace(legalName) ? null : legalName.Trim();
        contactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim().ToLowerInvariant();
        if (legalName is { Length: > MaxLegalNameLength })
            errors["legalName"] = [$"Unvan en fazla {MaxLegalNameLength} karakter olabilir."];
        if (contactEmail is not null && (contactEmail.Length > MaxContactEmailLength || !System.Net.Mail.MailAddress.TryCreate(contactEmail, out _)))
            errors["contactEmail"] = ["Geçerli bir iletişim e-postası girilmeli."];
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        LegalName = legalName;
        ContactEmail = contactEmail;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void ReplaceLogo(byte[] content, Guid? updatedBy, DateTimeOffset now)
    {
        var contentType = SniffLogoContentType(content);
        if (content.Length == 0 || content.Length > MaxLogoBytes || contentType is null)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["file"] = ["Logo PNG, JPEG veya WebP olmalı ve en fazla 512 KB olabilir."],
            });
        }

        Logo = content;
        LogoContentType = contentType;
        LogoVersion = Guid.NewGuid();
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    public void RemoveLogo(Guid? updatedBy, DateTimeOffset now)
    {
        Logo = null;
        LogoContentType = null;
        LogoVersion = null;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }

    // İstemcinin gönderdiği Content-Type'a güvenilmez; tür dosyanın ilk baytlarından çıkarılır.
    // SVG bilinçli olarak YOK: aynı origin'den image/svg+xml sunmak, dosya doğrudan açıldığında
    // gömülü script çalıştırabilir. Raster biçimler yeterli.
    public static string? SniffLogoContentType(ReadOnlySpan<byte> content)
    {
        if (content.Length >= 8 && content[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return "image/png";
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
            return "image/jpeg";
        if (content.Length >= 12 && content[..4].SequenceEqual("RIFF"u8) && content.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }

    [GeneratedRegex("^#[0-9a-f]{6}$")]
    private static partial Regex HexColor();
}
