using Abdera.Api.Shared.Branches;

namespace Abdera.Api.Modules.People.Domain;

// Okulun alt birimi (branş): müzikte enstrüman, dansta dans türü, sporda branş... Ad tarihsel
// (Abdera müzik okulu olarak başladı); kavram BranchCatalog'da genelleştirildi. Kategori arayüz
// terimlerini belirler. Kayıt ve derslerde kullanılan alt birim silinmez, pasife alınır.
public class Instrument
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string Category { get; private set; } = BranchCatalog.Music;
    public bool IsActive { get; private set; } = true;

    private Instrument() { }

    public static Instrument Create(string name, string code, string? category = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("İsim boş olamaz.", nameof(name));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Kod boş olamaz.", nameof(code));

        var instrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToUpperInvariant(),
        };
        instrument.Update(name, category ?? BranchCatalog.Music);
        return instrument;
    }

    public void Update(string name, string category)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("İsim boş olamaz.", nameof(name));
        // Katalog anahtarı ya da okulun açtığı alan ("c-..."); okulun alanlarında var olduğu kurulum/uç katmanında denetlenir.
        var key = BranchCatalog.Find(category)?.Key ?? (BranchCatalog.IsCustomKey(category?.ToLowerInvariant()) ? category!.ToLowerInvariant() : null)
            ?? throw new ArgumentException($"Bilinmeyen kategori: {category}", nameof(category));
        Name = name.Trim();
        Category = key;
    }

    public void SetActive(bool active) => IsActive = active;
}
