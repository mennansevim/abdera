namespace Abdera.Api.Shared;

// docs/10-decisions.md Q1 - veli varsayılan şifresi ad soyaddan türer:
//   {soyad, küçük harf}{adın ilk harfi, küçük}
// Örn: "Mennan Sevim" -> "sevimm", "Ayşe Nur Çelik" -> "celika".
// Türkçe karakterler ASCII'ye indirgenir ki veli şifreyi herhangi bir klavyeyle sorunsuz
// girebilsin; boşluk/tire gibi harf olmayan her şey atılır. Güvenlik: kamuya açık bilgiden
// türediği için YALNIZCA ilk şifredir - portal, bu şifreyle giren veliye değiştirmesini
// hatırlatır (GuardianAuth.ChangePasswordAsync). Tek kaynak: giriş yedeği, admin sıfırlama
// ve "varsayılan şifre mi" denetimi hep bunu kullanır.
public static class GuardianPasswordGenerator
{
    public static string Generate(string firstName, string lastName)
    {
        var first = OnlyLetters(ToAscii(firstName)).ToLowerInvariant();
        var last = OnlyLetters(ToAscii(lastName)).ToLowerInvariant();
        var initial = first.Length > 0 ? first[..1] : "";
        return $"{last}{initial}";
    }

    private static string ToAscii(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var sb = new System.Text.StringBuilder(input.Length);
        foreach (var ch in input)
        {
            sb.Append(ch switch
            {
                'ç' => "c", 'Ç' => "C",
                'ğ' => "g", 'Ğ' => "G",
                'ı' => "i", 'İ' => "I",
                'ö' => "o", 'Ö' => "O",
                'ş' => "s", 'Ş' => "S",
                'ü' => "u", 'Ü' => "U",
                _ => ch.ToString(),
            });
        }
        return sb.ToString();
    }

    private static string OnlyLetters(string input) =>
        new(input.Where(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z').ToArray());
}
