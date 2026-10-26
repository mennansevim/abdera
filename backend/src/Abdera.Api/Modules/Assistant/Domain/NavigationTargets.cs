namespace Abdera.Api.Modules.Assistant.Domain;

// "Yoklamayı nereden alırım?" gibi sorulardaki ekran adını uygulamanın menüsüne eşler.
// Yollar ve yalnız-yönetici ayrımı frontend'deki menüyle (app-header.tsx CORE_LINKS /
// ADMIN_LINKS) aynı olmalı; biri değişirse diğeri de değişmeli.
//
// Sıra önemli: daha özel ifade önce gelir ("fiyat" Aidatlar'dan, "öğrenci ekle"
// Öğrenciler'den önce). Anahtar sözcükler normalize edilmiş biçimdedir ve kelime başına
// önek olarak eşlenir ("aidat" → "aidatlar", "aidatı").
public static class NavigationTargets
{
    public record Target(string Key, string Label, string Path, bool AdminOnly, IReadOnlyList<string> Keywords);

    public static readonly IReadOnlyList<Target> All =
    [
        new("billing-policy", "Fiyat politikası", "/dashboard/billing", true, ["fiyat", "tarife", "indirim", "ucret"]),
        new("students-new", "Yeni öğrenci", "/dashboard/students/new", true, ["ogrenci ekle", "yeni ogrenci", "ogrenci kaydet", "kayit ac"]),
        new("change-requests", "Değişiklik talepleri", "/dashboard/change-requests", true, ["degisiklik talep", "talep"]),
        new("availability", "Müsaitlik", "/dashboard/availability", true, ["musait", "bos saat", "uygunluk"]),
        new("attendance", "Yoklama", "/dashboard/attendance", false, ["yoklama", "devam kaydi"]),
        new("billing", "Aidatlar", "/dashboard/billing", true, ["aidat", "tahsilat", "odeme", "borc"]),
        new("costs", "Gelir ve gider", "/dashboard/costs", true, ["gider", "masraf", "gelir tablo"]),
        new("banking", "Banka", "/dashboard/banking", true, ["banka", "havale", "iban", "eft"]),
        new("notifications", "Mesaj Merkezi", "/dashboard/notifications", true, ["mesaj merkez", "whatsapp", "bildirim"]),
        new("guardians", "Veli girişleri", "/dashboard/guardians", true, ["veli"]),
        new("backups", "Yedekleme", "/dashboard/backups", true, ["yedek"]),
        new("library", "Kütüphane", "/dashboard/library", false, ["kutuphane", "nota", "partisyon", "eser"]),
        new("shows", "Yıl Sonu Gösterisi", "/dashboard/shows", false, ["gosteri", "konser", "sahne"]),
        new("groups", "Grup dersleri", "/dashboard/groups", false, ["grup"]),
        new("calendar", "Takvim", "/dashboard/calendar", false, ["takvim", "ders program"]),
        new("progress", "Gelişim", "/dashboard/progress", false, ["gelisim", "ders notu", "odev", "karne"]),
        new("teachers", "Öğretmenler", "/dashboard/teachers", true, ["ogretmen"]),
        new("students", "Öğrenciler", "/dashboard/students", false, ["ogrenci"]),
        new("settings", "Ayarlar", "/dashboard/settings", false, ["ayar", "sifre", "yazi boyut"]),
        new("bug-report", "Hata bildir", "/dashboard/bug-report", false, ["hata", "sorun bildir"]),
    ];

    public static Target? Find(IReadOnlyList<string> tokens) =>
        All.FirstOrDefault(target => target.Keywords.Any(keyword => ContainsPhrase(tokens, keyword)));

    // Çok kelimeli ifadede kelimeler art arda gelmeli; her kelime önek olarak eşlenir
    // ("ders program" → "ders programi", "dersin programina").
    private static bool ContainsPhrase(IReadOnlyList<string> tokens, string phrase)
    {
        var words = phrase.Split(' ');
        for (var start = 0; start + words.Length <= tokens.Count; start++)
        {
            var matches = true;
            for (var offset = 0; offset < words.Length; offset++)
            {
                if (!tokens[start + offset].StartsWith(words[offset], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches) return true;
        }

        return false;
    }
}
