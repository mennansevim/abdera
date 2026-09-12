namespace Abdera.Api.Shared;

// docs/13-toplu-kurulum-ve-fix-list.md - veli ilk şifresi mnemonik bir desenle üretilir:
//   {ÇocukAdıİlk3, baş harf büyük}{VeliAdıİlk2, küçük}{TelefonSon4}
// Örn: çocuk "Zeynep", veli "Ayşe", telefon +90 532 123 45 67 -> "Zeyay4567".
// Türkçe karakterler ASCII'ye indirgenir ki veli WhatsApp'tan gelen şifreyi herhangi bir
// klavyeyle sorunsuz girebilsin. Güvenlik: kamuya açık bilgiden türer, YALNIZCA ilk şifredir
// (bkz. FIX-BACKLOG: "ilk girişte şifre değiştir"). Tek kaynak - hem toplu kurulum agent'ı
// hem admin "şifre gönder" akışı bunu kullanır.
public static class GuardianPasswordGenerator
{
    public static string Generate(string childFirstName, string guardianFirstName, string phoneNumber)
    {
        var child = OnlyLetters(ToAscii(childFirstName));
        var guardian = OnlyLetters(ToAscii(guardianFirstName));

        var digits = new string((phoneNumber ?? "").Where(char.IsDigit).ToArray());
        var last4 = digits.Length >= 4 ? digits[^4..] : digits.PadLeft(4, '0');

        var childPart = (child.Length >= 3 ? child[..3] : child.PadRight(3, 'x'));
        childPart = char.ToUpperInvariant(childPart[0]) + childPart[1..].ToLowerInvariant();

        var guardianPart = (guardian.Length >= 2 ? guardian[..2] : guardian.PadRight(2, 'x')).ToLowerInvariant();

        return $"{childPart}{guardianPart}{last4}";
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
