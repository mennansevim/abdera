using System.Text.RegularExpressions;

namespace Abdera.Platform.Api.Domain;

// Okul kısaltması: alan adı alt etiketi, compose proje adı ve sunucu dizini olarak kullanılır.
// Sunucu ajanının (deploy/host/abdera-host valid_slug) kuralıyla birebir aynı olmalı.
public static partial class Slug
{
    public static bool IsValid(string? value) =>
        value is not null && Pattern().IsMatch(value) && !value.EndsWith('-') && !value.Contains("--");

    // Ajanın "-drill" prova kopyası ve sistem adlarıyla çakışmasın.
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>
    {
        "platform", "www", "api", "admin", "mail", "status", "caddy", "abdera",
    };

    [GeneratedRegex("^[a-z][a-z0-9-]{2,30}$")]
    private static partial Regex Pattern();
}
