using System.Text;

namespace Abdera.Api.Shared;

// Kişi adını "Miray Sevim" biçimine getirir: her kelimenin ilk harfi büyük, kalanı küçük,
// fazla boşluklar teke iner. Tireli adlarda ("Ayşe-Nur") tireden sonraki harf de büyür.
//
// Türkçe i/ı ayrımı elle yapılır (i→İ, ı→I, I→ı, İ→i) - CultureInfo("tr-TR") kullanılmaz
// çünkü sonuç konteynerin ICU kurulumuna bağlı kalırdı (bkz. CLAUDE.md Dockerfile notu).
// Diğer harfler (ç ş ğ ö ü dahil) invariant Unicode eşlemesiyle doğru çevrilir. Aynı eşleme
// NormalizeStudentNames migration'ındaki SQL'de de var - biri değişirse diğeri de değişmeli.
public static class PersonNameFormatter
{
    public static string Format(string name)
    {
        var builder = new StringBuilder(name.Length);
        var startOfWord = true;
        var pendingSpace = false;

        foreach (var ch in name.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = true;
                startOfWord = true;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            if (ch == '-')
            {
                builder.Append(ch);
                startOfWord = true;
                continue;
            }

            builder.Append(startOfWord ? ToUpperTurkish(ch) : ToLowerTurkish(ch));
            startOfWord = false;
        }

        return builder.ToString();
    }

    private static char ToUpperTurkish(char ch) => ch switch
    {
        'i' => 'İ',
        'ı' => 'I',
        _ => char.ToUpperInvariant(ch),
    };

    private static char ToLowerTurkish(char ch) => ch switch
    {
        'I' => 'ı',
        'İ' => 'i',
        _ => char.ToLowerInvariant(ch),
    };
}
