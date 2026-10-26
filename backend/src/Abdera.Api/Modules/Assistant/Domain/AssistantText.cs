using System.Text;

namespace Abdera.Api.Modules.Assistant.Domain;

// Asistana yazılan serbest metni kurallara uygun biçime indirir: Türkçe harfler ASCII
// karşılığına düşer (ı/i, ş/s...), kesme işareti ve noktalama boşluk olur, rakamlar
// arasındaki ":" veya "." saat ayracı olarak ":"'ya döner ("17.30" → "17:30").
// "Mennan'ın dersini 17:00'ye al" → "mennan in dersini 17:00 ye al".
//
// Küçük harfe çevirme elle yapılır - CultureInfo("tr-TR") kullanılmaz, sonuç konteynerin
// ICU kurulumuna bağlı kalırdı (PersonNameFormatter ile aynı gerekçe).
public static class AssistantText
{
    public static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            var folded = Fold(ch);
            if (folded is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(folded);
                continue;
            }

            var isTimeSeparator = ch is ':' or '.'
                && i > 0 && char.IsAsciiDigit(value[i - 1])
                && i + 1 < value.Length && char.IsAsciiDigit(value[i + 1]);
            builder.Append(isTimeSeparator ? ':' : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static string[] Tokens(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static char Fold(char ch) => ch switch
    {
        'ı' or 'İ' or 'I' or 'î' or 'Î' => 'i',
        'ş' or 'Ş' => 's',
        'ğ' or 'Ğ' => 'g',
        'ü' or 'Ü' or 'û' or 'Û' => 'u',
        'ö' or 'Ö' => 'o',
        'ç' or 'Ç' => 'c',
        'â' or 'Â' => 'a',
        _ => char.ToLowerInvariant(ch),
    };
}
