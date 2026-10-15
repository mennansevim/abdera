namespace Abdera.Api.Shared.Branches;

// Okulun öğrettiği alanlar (docs/multi-tenant/ARCHITECTURE.md §5.6). Abdera yalnızca müzik okulu değil:
// dans, spor, dil, kodlama... hepsinde ders/seri/yoklama/aidat aynı; değişen tek şey "neyin dersi" sorusu.
//
// İki katman:
// - Kategori (Müzik, Dans, Spor...) sabit katalogdan seçilir; arayüz terimlerini belirler.
// - Alt birim (branş) okulun kendi listesidir: instruments tablosu (Instrument = Branş; ad tarihsel,
//   tablo yeniden adlandırılmadı). Katalogdaki öneriler yalnızca başlangıç; okul kendi alt birimini ekler.
//
// Katalog TEK kaynaktır: platform sihirbazı sürüm kaydındaki kopyasını okur (--print-catalog).
public sealed record BranchCategory(string Key, string Name, string UnitLabel, string UnitLabelPlural, IReadOnlyList<string> Suggestions);

public static class BranchCatalog
{
    public const string Music = "music";
    public const string Other = "other";

    public static readonly IReadOnlyList<BranchCategory> All =
    [
        new(Music, "Müzik", "Enstrüman", "Enstrümanlar",
            ["Piyano", "Gitar", "Keman", "Viyolonsel", "Bateri", "Şan", "Bağlama", "Flüt", "Ukulele", "Solfej"]),
        new("dance", "Dans", "Dans türü", "Dans türleri",
            ["Bale", "Modern dans", "Hip hop", "Latin", "Salsa", "Tango", "Halk dansları", "Çocuk dansı"]),
        new("drama", "Drama ve tiyatro", "Ders", "Dersler",
            ["Yaratıcı drama", "Tiyatro", "Diksiyon", "Doğaçlama"]),
        new("art", "Görsel sanatlar", "Atölye", "Atölyeler",
            ["Resim", "Karakalem", "Suluboya", "Seramik", "Heykel", "El sanatları"]),
        new("sport", "Spor", "Branş", "Branşlar",
            ["Taekwondo", "Karate", "Judo", "Jimnastik", "Yüzme", "Tenis", "Basketbol", "Voleybol", "Futbol"]),
        new("language", "Yabancı dil", "Ders", "Dersler",
            ["İngilizce - Konuşma", "İngilizce - Gramer", "İngilizce - Yazma", "İngilizce - Sınav hazırlık", "Almanca", "İspanyolca"]),
        new("technology", "Teknoloji ve kodlama", "Ders", "Dersler",
            ["Robotik kodlama", "Scratch", "Python", "Mobil uygulama", "3D tasarım", "Arduino"]),
        new("academic", "Akademik destek", "Ders", "Dersler",
            ["Matematik", "Fen bilimleri", "Türkçe", "LGS hazırlık", "Okuma-yazma"]),
        new("mind", "Zekâ ve strateji", "Ders", "Dersler",
            ["Satranç", "Akıl oyunları", "Mental aritmetik"]),
        new(Other, "Diğer", "Ders", "Dersler", []),
    ];

    public static BranchCategory? Find(string? key) =>
        All.FirstOrDefault(category => string.Equals(category.Key, key, StringComparison.OrdinalIgnoreCase));

    // Arayüz terimi: okulun tüm alt birimleri tek kategorideyse o kategorinin adı ("Enstrüman"),
    // birden fazla kategori varsa genel "Branş". Hiç alt birim yoksa da genel terim.
    public static (string Singular, string Plural) TermsFor(IReadOnlyCollection<string> categoryKeys)
    {
        var distinct = categoryKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return distinct.Count == 1 && Find(distinct[0]) is { } only
            ? (only.UnitLabel, only.UnitLabelPlural)
            : ("Branş", "Branşlar");
    }

    // Alt birim kodu addan türetilir (büyük harf, Türkçe harfler sadeleştirilir); okul içinde benzersiz.
    public static string CodeFor(string name)
    {
        var map = new Dictionary<char, char> { ['ç'] = 'C', ['ğ'] = 'G', ['ı'] = 'I', ['i'] = 'I', ['ö'] = 'O', ['ş'] = 'S', ['ü'] = 'U',
            ['Ç'] = 'C', ['Ğ'] = 'G', ['İ'] = 'I', ['Ö'] = 'O', ['Ş'] = 'S', ['Ü'] = 'U' };
        var chars = name.Trim().Select(c => map.TryGetValue(c, out var m) ? m : char.ToUpperInvariant(c))
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray();
        var code = string.Join('_', new string(chars).Split('_', StringSplitOptions.RemoveEmptyEntries));
        return code.Length == 0 ? "DERS" : code.Length > 20 ? code[..20] : code;
    }
}
