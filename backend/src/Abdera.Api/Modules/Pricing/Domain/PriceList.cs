namespace Abdera.Api.Modules.Pricing.Domain;

// docs/03-erd.md - Pricing > price_lists. Bir fiyat "dönemi" (örn. "2026-2027 Sezonu").
// docs/10-decisions.md A1: fiyat değişikliği geçmişe dönük çalışmaz - Receivable oluşurken
// tutar snapshot alınır, bu listeye sonradan dokunmak geçmiş kayıtları etkilemez.
public class PriceList
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }

    private PriceList() { }

    public static PriceList Create(string name, DateOnly effectiveFrom, DateOnly? effectiveUntil, Guid createdBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("İsim boş olamaz.", nameof(name));
        if (effectiveUntil is { } until && until < effectiveFrom)
            throw new ArgumentException("Bitiş tarihi başlangıçtan önce olamaz.", nameof(effectiveUntil));

        return new PriceList
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            EffectiveFrom = effectiveFrom,
            EffectiveUntil = effectiveUntil,
            CreatedAt = now,
            CreatedBy = createdBy,
        };
    }
}
