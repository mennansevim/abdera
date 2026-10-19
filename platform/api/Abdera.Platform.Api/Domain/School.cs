namespace Abdera.Platform.Api.Domain;

public enum SchoolStatus
{
    Provisioning,
    Active,
    Failed,
    Suspended,
    PendingDestroy,
    Destroyed,
}

// Bir okul kurulumu (docs/multi-tenant/ARCHITECTURE.md §3.2). Platform okulun kişisel verisini
// tutmaz; yalnızca kimliği, nerede çalıştığı, sürümü ve modülleri.
public class School
{
    public Guid Id { get; private set; }
    public string Slug { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string ShortName { get; private set; } = null!;
    public SchoolStatus Status { get; private set; }
    public Guid? HostId { get; private set; }
    public string PrimaryDomain { get; private set; } = null!;
    public string? AppVersion { get; private set; }
    public string? TargetVersion { get; private set; }
    public List<string> Modules { get; private set; } = [];
    // Okulun öğrettiği alan kategorileri (music, dance, sport...; §5.6). Yalnızca panelde gösterim ve
    // süzme için; alt birimlerin kendisi okulun veritabanındadır, okul yöneticisi sonradan değiştirebilir.
    public List<string> Categories { get; private set; } = [];
    // Okulun kendi alan adı (ör. panel.okulum.com): asıl adresin (platform alt alanı) yanına eklenir.
    // Önce bekleyen kaydedilir; okulun DNS'i sunucuya çözülünce bağlanır ve etkin olur.
    public string? CustomDomain { get; private set; }
    public bool CustomDomainRedirect { get; private set; }
    public bool CustomDomainActive { get; private set; }
    public string? CustomDomainCheck { get; private set; }
    public DateTimeOffset? CustomDomainCheckedAt { get; private set; }
    // Okula girilen adres: bağlı alan adı varsa o, yoksa platform alt alanı.
    public string PublicDomain => CustomDomainActive && CustomDomain is not null ? CustomDomain : PrimaryDomain;
    // Okulun kendi açtığı alanların adları (anahtar "c-..." → ad); katalog alanlarının adı sürüm kataloğundan.
    public string CustomCategoryNamesJson { get; private set; } = "{}";
    public IReadOnlyDictionary<string, string> CustomCategoryNames =>
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(CustomCategoryNamesJson) ?? [];
    public string? PrimaryColor { get; private set; }
    // Filo güncellemesinde ilk dalga (kanarya) ve güncellemeden muaf tutma (§7).
    public bool IsCanary { get; private set; }
    public bool IsVersionPinned { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint RowVersion { get; private set; }

    private School() { }

    public static School Create(
        string slug, string displayName, string shortName, string primaryDomain, string version,
        IEnumerable<string> modules, string? primaryColor, DateTimeOffset now)
    {
        if (!Domain.Slug.IsValid(slug) || Domain.Slug.Reserved.Contains(slug))
            throw ValidationFailedException.For("slug", "Kısaltma 3-31 karakter, küçük harf/rakam/tire olmalı ve ayrılmış bir ad olmamalı.");
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 120)
            throw ValidationFailedException.For("displayName", "Okul adı 1-120 karakter olmalı.");
        if (string.IsNullOrWhiteSpace(shortName) || shortName.Length > 40)
            throw ValidationFailedException.For("shortName", "Kısa ad 1-40 karakter olmalı.");

        return new School
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            DisplayName = displayName.Trim(),
            ShortName = shortName.Trim(),
            PrimaryDomain = primaryDomain,
            TargetVersion = version,
            Modules = modules.Distinct().OrderBy(key => key, StringComparer.Ordinal).ToList(),
            PrimaryColor = primaryColor,
            Status = SchoolStatus.Provisioning,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void AssignHost(Guid hostId, DateTimeOffset now) { HostId = hostId; UpdatedAt = now; }

    public void MarkActive(string version, DateTimeOffset now)
    {
        Status = SchoolStatus.Active;
        AppVersion = version;
        TargetVersion = null;
        UpdatedAt = now;
    }

    public void MarkFailed(DateTimeOffset now) { Status = SchoolStatus.Failed; UpdatedAt = now; }
    public void MarkProvisioning(DateTimeOffset now) { Status = SchoolStatus.Provisioning; UpdatedAt = now; }

    public void Suspend(DateTimeOffset now)
    {
        if (Status != SchoolStatus.Active) throw new ConflictException("Yalnızca etkin okul askıya alınabilir.");
        Status = SchoolStatus.Suspended;
        UpdatedAt = now;
    }

    public void Resume(DateTimeOffset now)
    {
        if (Status != SchoolStatus.Suspended) throw new ConflictException("Yalnızca askıdaki okul devam ettirilebilir.");
        Status = SchoolStatus.Active;
        UpdatedAt = now;
    }

    public void MarkPendingDestroy(DateTimeOffset now) { Status = SchoolStatus.PendingDestroy; UpdatedAt = now; }
    // Silinen okulun alan adı serbest kalır (custom_domain tekil indeksi başka okula verilmesini engellemesin).
    public void MarkDestroyed(DateTimeOffset now)
    {
        Status = SchoolStatus.Destroyed;
        HostId = null;
        ClearDomain(now);
    }
    public void RestoreFromPendingDestroy(DateTimeOffset now) { Status = SchoolStatus.Active; UpdatedAt = now; }

    public void SetTargetVersion(string version, DateTimeOffset now) { TargetVersion = version; UpdatedAt = now; }
    public void ClearTargetVersion(DateTimeOffset now) { TargetVersion = null; UpdatedAt = now; }
    public void CompleteUpgrade(string version, DateTimeOffset now) { AppVersion = version; TargetVersion = null; UpdatedAt = now; }
    public void SetCanary(bool canary, DateTimeOffset now) { IsCanary = canary; UpdatedAt = now; }
    public void SetVersionPinned(bool pinned, DateTimeOffset now) { IsVersionPinned = pinned; UpdatedAt = now; }
    public void SetPendingDomain(string domain, bool redirect, DateTimeOffset now)
    {
        if (CustomDomain != domain) CustomDomainActive = false;
        CustomDomain = domain;
        CustomDomainRedirect = redirect;
        CustomDomainCheck = null;
        CustomDomainCheckedAt = null;
        UpdatedAt = now;
    }

    public void RecordDomainCheck(string result, DateTimeOffset now)
    {
        CustomDomainCheck = result.Length > 300 ? result[..300] : result;
        CustomDomainCheckedAt = now;
        UpdatedAt = now;
    }

    public void ActivateDomain(DateTimeOffset now) { CustomDomainActive = CustomDomain is not null; UpdatedAt = now; }

    public void ClearDomain(DateTimeOffset now)
    {
        CustomDomain = null;
        CustomDomainRedirect = false;
        CustomDomainActive = false;
        CustomDomainCheck = null;
        CustomDomainCheckedAt = null;
        UpdatedAt = now;
    }

    public void SetCategories(IEnumerable<string> categories, DateTimeOffset now, IReadOnlyDictionary<string, string>? customNames = null)
    {
        CustomCategoryNamesJson = System.Text.Json.JsonSerializer.Serialize(customNames ?? new Dictionary<string, string>());
        Categories = categories.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
        UpdatedAt = now;
    }

    public void SetModules(IEnumerable<string> modules, DateTimeOffset now)
    {
        Modules = modules.Distinct().OrderBy(key => key, StringComparer.Ordinal).ToList();
        UpdatedAt = now;
    }
    public void Seen(DateTimeOffset now) => LastSeenAt = now;

    // Okulun .env'ine yazılan değer: boş liste "none" olur (boş değer eski kurulumda "hepsi açık" demek).
    public string ModulesEnvValue => Modules.Count == 0 ? "none" : string.Join(',', Modules);
}
