using Abdera.Api.Shared.Branches;

namespace Abdera.Api.Modules.People.Domain;

// Okulun kendi açtığı eğitim alanı (katalogda olmayan: "Yoga ve meditasyon", "Mutfak atölyesi"...).
// Anahtarı "c-" önekli ve addan türetilir; katalog anahtarlarıyla çakışmaz. Alt birim adı ("Ders",
// "Atölye") ekranlardaki terimi belirler. Kullanılan alan silinmez; ad ve terim değiştirilebilir.
public class CustomBranchCategory
{
    public Guid Id { get; private set; }
    public string Key { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string UnitLabel { get; private set; } = null!;
    public string UnitLabelPlural { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private CustomBranchCategory() { }

    public static CustomBranchCategory Create(string key, string name, string? unitLabel, string? unitLabelPlural, DateTimeOffset now)
    {
        if (!BranchCatalog.IsCustomKey(key)) throw new ArgumentException($"Geçersiz alan anahtarı: {key}", nameof(key));
        var category = new CustomBranchCategory { Id = Guid.NewGuid(), Key = key, CreatedAt = now };
        category.Update(name, unitLabel, unitLabelPlural, now);
        return category;
    }

    public void Update(string name, string? unitLabel, string? unitLabelPlural, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 60) throw new ArgumentException("Alan adı 1-60 karakter olmalı.", nameof(name));
        Name = name.Trim();
        UnitLabel = string.IsNullOrWhiteSpace(unitLabel) ? "Ders" : unitLabel.Trim();
        UnitLabelPlural = string.IsNullOrWhiteSpace(unitLabelPlural) ? BranchCatalog.PluralOf(UnitLabel) : unitLabelPlural.Trim();
        if (UnitLabel.Length > 40 || UnitLabelPlural.Length > 40) throw new ArgumentException("Terim en fazla 40 karakter olabilir.", nameof(unitLabel));
        UpdatedAt = now;
    }

    public BranchCategory ToCategory() => new(Key, Name, UnitLabel, UnitLabelPlural, []);
}
