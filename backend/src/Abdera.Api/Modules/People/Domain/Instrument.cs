namespace Abdera.Api.Modules.People.Domain;

// docs/03-erd.md - People > instruments. Seed verisi (PIANO/GUITAR/VIOLIN/DRUMS)
// migration ile gelir (docs/08-migrations.md 009_seed_reference_data).
public class Instrument
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Code { get; private set; } = null!;

    private Instrument() { }

    public static Instrument Create(string name, string code)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("İsim boş olamaz.", nameof(name));
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Kod boş olamaz.", nameof(code));

        return new Instrument
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Code = code.Trim().ToUpperInvariant(),
        };
    }
}
