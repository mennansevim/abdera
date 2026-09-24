using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Library.Domain;

// Öğretmen veya yöneticinin kütüphaneye kendisinin eklediği eser (katalogdaki kitap ve Mutopia
// kayıtlarından ayrı). Kitap eserleri gibi yalnızca panelde görünür; PDF'i ScoreFile'da
// "piece-<id>" kimliğiyle durur. Ekleyen öğretmen ve yönetici düzenleyip silebilir.
public class LibraryPiece
{
    // Kütüphane ekranındaki enstrüman/koleksiyon anahtarlarıyla aynı (frontend sheet-music.ts).
    public static readonly string[] Instruments = ["piano", "violin", "guitar", "drums", "flute", "cello", "voice", "organ", "other"];
    public static readonly string[] Categories = ["education", "classical", "popular", "world", "children"];

    public Guid Id { get; private set; }
    public string Title { get; private set; } = null!;
    public string Composer { get; private set; } = null!;
    public string Instrument { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public int? Level { get; private set; }
    public string? Notes { get; private set; }
    // Ekleyenin hesabı silinirse eser okulun kütüphanesinde kalır, bu alan boşalır (PersonEraser).
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public string EntryId => EntryIdFor(Id);

    private LibraryPiece() { }

    public static string EntryIdFor(Guid id) => $"piece-{id:N}";

    public static LibraryPiece Create(string title, string composer, string instrument, string category, int? level, string? notes, Guid createdByUserId, DateTimeOffset now)
    {
        var piece = new LibraryPiece { Id = Guid.NewGuid(), CreatedByUserId = createdByUserId, CreatedAt = now };
        piece.Update(title, composer, instrument, category, level, notes, now);
        return piece;
    }

    public void Update(string title, string composer, string instrument, string category, int? level, string? notes, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        title = (title ?? string.Empty).Trim();
        composer = (composer ?? string.Empty).Trim();
        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (title.Length is 0 or > 200) errors["title"] = ["Eser adı 1-200 karakter olmalı."];
        if (composer.Length > 200) errors["composer"] = ["Besteci en fazla 200 karakter olabilir."];
        if (!Instruments.Contains(instrument)) errors["instrument"] = ["Geçersiz enstrüman."];
        if (!Categories.Contains(category)) errors["category"] = ["Geçersiz koleksiyon."];
        if (level is < 1 or > 5) errors["level"] = ["Seviye 1-5 arasında olmalı."];
        if (notes is { Length: > 1000 }) errors["notes"] = ["Not en fazla 1000 karakter olabilir."];
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        Title = title;
        Composer = composer;
        Instrument = instrument;
        Category = category;
        Level = level;
        Notes = notes;
        UpdatedAt = now;
    }

    public bool CanBeEditedBy(Guid userId, bool isAdmin) => isAdmin || CreatedByUserId == userId;
}
