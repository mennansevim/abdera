using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Abdera.Api.Modules.Assistant.Domain;

// Dil modeline giden metinde kişi adları kodla değişir (docs/10-decisions.md AE9 - KVKK, kullanıcı
// kararı "takma adla"). Model "Deniz Arslan"ı değil "Ö-K7Q2"yi görür; araç sonuçları da kodla
// döner, cevap ekrana gelmeden kodlar gerçek adlara çevrilir.
//
// Kod kişi kimliğinden türetilir (aynı kişi her mesajda aynı kodu alır), böylece istemcide
// saklanan maskeli sohbet geçmişi sonraki mesajlarda da anlamlı kalır.
//
// Maskelenen: öğrenci ve öğretmenin ad + soyadı; tek başına ad ya da soyad yalnızca okulda
// TEK kişiye aitse (ör. "Deniz" tek). Ortak soyad ("Arslan" iki kardeş) ya da öğretmenle ortak
// soyad olduğu gibi kalır: tek başına bir kişiyi belirtmez, model bunu find_students ile arar.
public sealed partial class Pseudonymizer
{
    public record Person(Guid Id, string FirstName, string LastName, bool IsTeacher);

    private sealed record Entry(Person Person, string Code, string FullName);

    private static readonly System.Globalization.CultureInfo Turkish = new("tr-TR");

    private readonly List<Entry> _entries;
    private readonly Dictionary<string, Entry> _byCode;
    private readonly List<(Regex Pattern, Entry Entry)> _patterns = [];

    public Pseudonymizer(IEnumerable<Person> people)
    {
        _entries = people
            .DistinctBy(person => person.Id)
            .Select(person => new Entry(person, CodeOf(person), $"{person.FirstName} {person.LastName}"))
            .ToList();
        _byCode = _entries.ToDictionary(entry => entry.Code, StringComparer.Ordinal);

        // Önce ad + soyad (uzun olan önce), sonra tekil ad, sonra tekil soyad.
        foreach (var entry in _entries.OrderByDescending(entry => entry.FullName.Length))
            _patterns.Add((NameRegex(entry.FullName), entry));

        foreach (var group in _entries.Where(entry => !entry.Person.IsTeacher).GroupBy(entry => Key(entry.Person.FirstName)))
        {
            if (group.Count() == 1 && group.Key.Length >= 3 && !_entries.Any(other => other.Person.IsTeacher && Key(other.Person.FirstName) == group.Key))
                _patterns.Add((NameRegex(group.First().Person.FirstName), group.First()));
        }

        foreach (var group in _entries.GroupBy(entry => Key(entry.Person.LastName)))
        {
            if (group.Count() == 1 && group.Key.Length >= 3 && !group.First().Person.IsTeacher)
                _patterns.Add((NameRegex(group.First().Person.LastName), group.First()));
        }
    }

    public string CodeFor(Guid id) => _entries.First(entry => entry.Person.Id == id).Code;

    public bool TryResolve(string code, out Guid id)
    {
        if (_byCode.TryGetValue(code.Trim(), out var entry))
        {
            id = entry.Person.Id;
            return true;
        }

        id = Guid.Empty;
        return false;
    }

    public string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        foreach (var (pattern, entry) in _patterns)
        {
            text = pattern.Replace(text, match =>
            {
                var suffix = match.Groups["suffix"].Value;
                // Ada ardından gelen harfler bilinen bir ek değilse bu bir ad değil, başka bir
                // kelimedir ("Ecevit"); dokunulmaz.
                if (suffix.Length > 0 && !suffix.StartsWith('\'') && !suffix.StartsWith('’') && !IsKnownSuffix(suffix)) return match.Value;
                var cleanSuffix = suffix.TrimStart('\'', '’').ToLower(Turkish);
                return cleanSuffix.Length > 0 ? $"{entry.Code}'{cleanSuffix}" : entry.Code;
            });
        }

        return text;
    }

    // Kodları adlara çevirir; kodun ardındaki eki adın ünlü uyumuna göre yeniden kurar
    // ("Ö-K7Q2'nin" → "Deniz Arslan'ın").
    public string Unmask(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        return CodePattern().Replace(text, match =>
        {
            if (!_byCode.TryGetValue(match.Groups["code"].Value, out var entry)) return match.Value;
            var suffix = match.Groups["suffix"].Value;
            return suffix.Length == 0 ? entry.FullName : $"{entry.FullName}'{TurkishSuffix.Harmonize(entry.Person.LastName, suffix)}";
        });
    }

    private static string CodeOf(Person person)
    {
        var hash = SHA256.HashData(person.Id.ToByteArray());
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var code = new StringBuilder(person.IsTeacher ? "T-" : "Ö-");
        for (var i = 0; i < 4; i++) code.Append(alphabet[hash[i] % alphabet.Length]);
        return code.ToString();
    }

    private static string Key(string value) => AssistantText.Normalize(value);

    private static bool IsKnownSuffix(string suffix) => KnownSuffix().IsMatch(AssistantText.Normalize(suffix));

    // Türkçe harfler ve büyük/küçük farkı gözetmeden ad; ardından gelen ek "suffix" grubunda.
    private static Regex NameRegex(string name)
    {
        // "-" de dışarıda: kodun içindeki harfler ("Ö-ECEA") bir ad sanılmasın.
        var pattern = new StringBuilder(@"(?<![\p{L}\p{N}\-])");
        foreach (var ch in name.Trim())
        {
            pattern.Append(char.IsWhiteSpace(ch) ? @"\s+" : CharClass(ch));
        }

        pattern.Append(@"(?<suffix>['’]?\p{L}*)");
        return new Regex(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string CharClass(char ch) => AssistantText.Normalize(ch.ToString()) switch
    {
        "i" => "[iıİI]",
        "s" => "[sşSŞ]",
        "g" => "[gğGĞ]",
        "u" => "[uüUÜ]",
        "o" => "[oöOÖ]",
        "c" => "[cçCÇ]",
        var folded when folded.Length == 1 => Regex.Escape(folded),
        _ => Regex.Escape(ch.ToString()),
    };

    [GeneratedRegex(@"(?<code>[ÖT]-[A-Z2-9]{4})(?:['’](?<suffix>\p{L}+))?")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^(n?[iu]n|y?[ae]|y?[iu]|n[ae]|[dt][ae]|n?[dt][ae]n|y?l[ae]|[dt][ae]ki|ni|nu)$")]
    private static partial Regex KnownSuffix();
}

// Ad + Türkçe hal eki: modelin koda eklediği ek (çoğu zaman yanlış ünlüyle) adın son ünlüsüne
// ve son harfine göre yeniden kurulur. Tanınmayan ek olduğu gibi bırakılır.
public static class TurkishSuffix
{
    private const string Vowels = "aeıioöuüAEIİOÖUÜ";
    private const string HardConsonants = "çfhkpsştÇFHKPSŞT";

    public static string Harmonize(string word, string suffix)
    {
        var lastVowel = word.LastOrDefault(ch => Vowels.Contains(ch));
        var back = "aıouAIOU".Contains(lastVowel);
        var rounded = "oöuüOÖUÜ".Contains(lastVowel);
        var endsWithVowel = word.Length > 0 && Vowels.Contains(word[^1]);
        var hard = word.Length > 0 && HardConsonants.Contains(word[^1]);
        var a = back ? "a" : "e";
        var i = (back, rounded) switch { (true, false) => "ı", (true, true) => "u", (false, false) => "i", (false, true) => "ü" };
        var d = hard ? "t" : "d";

        var normalized = AssistantText.Normalize(suffix);
        return normalized switch
        {
            "nin" or "nun" or "in" or "un" => (endsWithVowel ? "n" : "") + i + "n",
            "ye" or "ya" or "e" or "a" => (endsWithVowel ? "y" : "") + a,
            "yi" or "yu" or "i" or "u" => (endsWithVowel ? "y" : "") + i,
            "de" or "da" or "te" or "ta" => d + a,
            "den" or "dan" or "ten" or "tan" => d + a + "n",
            "le" or "la" or "yle" or "yla" => (endsWithVowel ? "y" : "") + "l" + a,
            "deki" or "daki" or "teki" or "taki" => d + a + "ki",
            _ => suffix,
        };
    }
}
