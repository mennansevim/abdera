using System.Text.RegularExpressions;

namespace Abdera.Platform.Api.Domain;

// CI'ın GHCR'ye ittiği sürüm (§3.2 releases, §16.4, §16.8): imajlar digest ile, modül kataloğu
// sürümle birlikte. Henüz kurulmamış bir okulun modülleri buradan okunur.
public partial class Release
{
    public string Version { get; private set; } = null!;
    public string ApiImage { get; private set; } = null!;
    public string WebImage { get; private set; } = null!;
    public string? Revision { get; private set; }
    public string ModuleCatalogJson { get; private set; } = "[]";
    public string? Notes { get; private set; }
    public bool IsWithdrawn { get; private set; }
    public DateTimeOffset ReleasedAt { get; private set; }

    private Release() { }

    public static Release Create(string version, string apiImage, string webImage, string? revision, string moduleCatalogJson, string? notes, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (!VersionPattern().IsMatch(version ?? "")) errors["version"] = ["Sürüm YYYY.MM.DD.N biçiminde olmalı."];
        if (!ImagePattern().IsMatch(apiImage ?? "")) errors["apiImage"] = ["API imajı digest ile verilmeli (…@sha256:…)."];
        if (!ImagePattern().IsMatch(webImage ?? "")) errors["webImage"] = ["Web imajı digest ile verilmeli (…@sha256:…)."];
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        return new Release
        {
            Version = version!,
            ApiImage = apiImage!,
            WebImage = webImage!,
            Revision = revision,
            ModuleCatalogJson = moduleCatalogJson,
            Notes = notes,
            ReleasedAt = now,
        };
    }

    public void Withdraw() => IsWithdrawn = true;

    // Sürümler "YYYY.MM.DD.N"; parça parça sayısal karşılaştırılır (geri sürüme yükseltme olmasın).
    public static int Compare(string left, string right)
    {
        var a = left.Split('.').Select(int.Parse).ToArray();
        var b = right.Split('.').Select(int.Parse).ToArray();
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        return a.Length.CompareTo(b.Length);
    }

    [GeneratedRegex(@"^\d{4}\.\d{2}\.\d{2}\.\d+$")]
    public static partial Regex VersionPattern();

    // Ajanın valid_image kuralıyla aynı.
    [GeneratedRegex(@"^[a-z0-9.-]+(:[0-9]+)?/[a-z0-9._/-]+@sha256:[a-f0-9]{64}$")]
    public static partial Regex ImagePattern();
}
