using System.Text.RegularExpressions;
using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Library.Domain;

// Öğretmenin (yalnızca kendi öğrencisine) veya yöneticinin kütüphaneden bir öğrenciye önerdiği
// eser. Katalog (kitap, Mutopia) frontend'de durduğu için eser adı ve bestecisi öneri anında
// satıra donar: katalog sonradan değişse de öğrencinin listesi okunur kalır.
public partial class LibrarySuggestion
{
    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public string EntryId { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string Composer { get; private set; } = null!;
    public string? Note { get; private set; }
    // Öneren öğretmen; yönetici önerisinde null. Öğretmen silinince de null olur (PersonEraser).
    public Guid? TeacherId { get; private set; }
    public string SuggestedByName { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private LibrarySuggestion() { }

    public static LibrarySuggestion Create(Guid studentId, string entryId, string title, string composer, string? note, Guid? teacherId, string suggestedByName, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        title = (title ?? string.Empty).Trim();
        composer = (composer ?? string.Empty).Trim();
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (!EntryIdPattern().IsMatch(entryId ?? string.Empty)) errors["entryId"] = ["Geçersiz eser kimliği."];
        if (title.Length is 0 or > 200) errors["title"] = ["Eser adı 1-200 karakter olmalı."];
        if (composer.Length > 200) errors["composer"] = ["Besteci en fazla 200 karakter olabilir."];
        if (note is { Length: > 500 }) errors["note"] = ["Not en fazla 500 karakter olabilir."];
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        return new LibrarySuggestion
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            EntryId = entryId!,
            Title = title,
            Composer = composer,
            Note = note,
            TeacherId = teacherId,
            SuggestedByName = suggestedByName,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    [GeneratedRegex("^(book|mutopia|piece)-[A-Za-z0-9-]{1,90}$")]
    private static partial Regex EntryIdPattern();
}
