using System.Text.RegularExpressions;

namespace Abdera.Api.Modules.Assistant.Domain;

public enum AssistantIntentKind
{
    Unknown,
    Progress,   // "Mennan Sevim'in gelişimini getir"
    Dues,       // "Aidat günü gelenleri listele"
    Reschedule, // "Perşembe dersini 17:00'ye al"
    Makeup,     // "Ece'ye telafi oluştur"
    Lessons,    // "Mennan'ın dersleri"
    Navigate,   // "Yoklamayı nereden alırım?"
}

public enum RescheduleScope
{
    Unspecified,
    Once,      // "bu seferlik", "bir kerelik"
    Recurring, // "her hafta", "bundan sonra"
}

// Asistanın kural yolu (docs/10-decisions.md AE): serbest metinden niyeti, gün/saat
// ifadelerini ve kapsamı çıkarır. Veritabanına ve saate dokunmaz; öğrenci adı eşleme
// StudentNameMatcher'da, tarihe çevirme DayReference.Resolve'dadır.
//
// Eşleşmeyen metin Unknown döner ve tahmin yapılmaz - yanlış öğrencinin dersini taşımaktansa
// "anlamadım" demek daha ucuz. LLM yolu eklendiğinde Unknown'lar oraya düşecek.
public static partial class IntentParser
{
    public record ParsedRequest(
        AssistantIntentKind Kind,
        DayReference? SourceDay,
        TimeOnly? SourceTime,
        DayReference? TargetDay,
        TimeOnly? TargetTime,
        RescheduleScope Scope,
        NavigationTargets.Target? Navigation);

    private enum Role { Undecided, Source, Target }

    private static readonly (string Stem, DayOfWeek Day)[] Weekdays =
    [
        // "cumartesi" "cuma"dan, "pazartesi" "pazar"dan önce denenmeli.
        ("cumartesi", DayOfWeek.Saturday),
        ("pazartesi", DayOfWeek.Monday),
        ("carsamba", DayOfWeek.Wednesday),
        ("persembe", DayOfWeek.Thursday),
        ("cuma", DayOfWeek.Friday),
        ("sali", DayOfWeek.Tuesday),
        ("pazar", DayOfWeek.Sunday),
    ];

    private static readonly (string Stem, int Offset)[] RelativeDays = [("bugun", 0), ("yarin", 1)];

    // Yönelme eki ("cumaya", "17:00'ye") hedefi, "-ki"/"-den" eki ("perşembeki", "16'daki",
    // "perşembeden") kaynağı gösterir.
    private static readonly HashSet<string> TargetSuffixes = ["a", "e", "ya", "ye"];
    private static readonly HashSet<string> SourceSuffixes = ["ki", "ku", "daki", "deki", "taki", "teki", "dan", "den", "tan", "ten", "ndan", "nden"];
    private static readonly HashSet<string> NeutralDaySuffixes = ["", "i", "u", "yi", "nin", "in", "gunu", "da", "de"];
    private static readonly HashSet<string> DayFillers = ["gunu", "gunku", "gunki", "saat", "saatteki", "saatindeki"];

    private static readonly string[] RecurringPhrases = ["her hafta", "surekli", "kalici", "bundan sonra", "artik", "tum ders", "butun ders"];
    private static readonly string[] OncePhrases = ["bir kerelik", "tek seferlik", "bu seferlik", "seferlik", "sadece bu", "yalniz bu", "bu hafta"];

    private static readonly string[] MovePrefixes = ["tasi", "ertele", "kaydir", "degistir", "guncelle", "oynat"];
    private static readonly HashSet<string> MoveWords = ["al", "alalim", "alin", "alsin", "alir", "alabilir", "aliver", "cek", "cekelim", "cekin", "yap", "yapalim", "yapin"];
    private static readonly HashSet<string> NavigateWords = ["git", "gidelim", "gotur", "ac", "acar", "acsana", "acin"];
    private static readonly HashSet<string> WhereWords = ["nerede", "nereden", "nerden", "nereye", "neresi", "nerde"];

    public static ParsedRequest Parse(string text)
    {
        var tokens = AssistantText.Tokens(AssistantText.Normalize(text));

        var (sourceDay, targetDay) = ExtractDays(tokens);
        var (sourceTime, targetTime) = ExtractTimes(tokens);
        var scope = HasAnyPhrase(tokens, RecurringPhrases) || HasEveryWeekday(tokens) ? RescheduleScope.Recurring
            : HasAnyPhrase(tokens, OncePhrases) ? RescheduleScope.Once
            : RescheduleScope.Unspecified;
        var navigation = NavigationTargets.Find(tokens);

        var kind = DetectKind(tokens, navigation, targetDay, targetTime);
        return new ParsedRequest(kind, sourceDay, sourceTime, targetDay, targetTime, scope,
            kind == AssistantIntentKind.Navigate ? navigation : null);
    }

    private static AssistantIntentKind DetectKind(string[] tokens, NavigationTargets.Target? navigation, DayReference? targetDay, TimeOnly? targetTime)
    {
        // Ekran sorusu önce: "aidatlar nerede" Aidatlar ekranıdır, aidat listesi değil.
        if (navigation is not null && (IsWhereQuestion(tokens) || tokens.Any(NavigateWords.Contains)))
            return AssistantIntentKind.Navigate;

        if (HasPrefix(tokens, "aidat", "odeme", "borc", "vade", "tahsil")) return AssistantIntentKind.Dues;
        if (HasPrefix(tokens, "telafi")) return AssistantIntentKind.Makeup;
        if (HasPrefix(tokens, "gelisim", "ilerleme", "karne", "basari") || HasPhrase(tokens, "nasil gidiyor"))
            return AssistantIntentKind.Progress;

        var hasTarget = targetDay is not null || targetTime is not null;
        var mentionsLesson = HasPrefix(tokens, "ders", "saat");
        if ((HasPrefix(tokens, MovePrefixes) && (mentionsLesson || hasTarget)) || (tokens.Any(MoveWords.Contains) && hasTarget))
            return AssistantIntentKind.Reschedule;

        if (HasPrefix(tokens, "ders", "program")) return AssistantIntentKind.Lessons;

        // Soru ya da "aç/git" olmadan ekran adı yalnızca kısa mesajda yönlendirmedir
        // ("Kütüphane", "yoklama ekranı"). Uzun cümlede geçen "öğrenciler" kelimesi ekran
        // isteği değildir ("en çok devamsızlık yapan öğrenciler kim?").
        return navigation is not null && tokens.Length <= 2 ? AssistantIntentKind.Navigate : AssistantIntentKind.Unknown;
    }

    // "nerede/nereden" ya da "nasıl alınır/eklenir/yaparım" (edilgen veya birinci tekil fiil).
    // "Nasıl gidiyor" ekran sorusu değildir; gelişim sorusudur.
    private static bool IsWhereQuestion(string[] tokens)
    {
        if (tokens.Any(WhereWords.Contains)) return true;
        for (var i = 0; i + 1 < tokens.Length; i++)
        {
            if (tokens[i] == "hangi" && (tokens[i + 1].StartsWith("ekran", StringComparison.Ordinal) || tokens[i + 1].StartsWith("sayfa", StringComparison.Ordinal)))
                return true;
            if (tokens[i] == "nasil" && HowToVerb().IsMatch(tokens[i + 1])) return true;
        }

        return false;
    }

    private static (DayReference? Source, DayReference? Target) ExtractDays(string[] tokens)
    {
        var found = new List<(Role Role, DayReference Day)>();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!TryReadDay(tokens[i], out var day, out var suffix)) continue;

            var nextWeek = i > 0 && tokens[i - 1] == "haftaya";
            if (nextWeek) day = day with { NextWeek = true };

            Role role;
            if (TargetSuffixes.Contains(suffix)) role = Role.Target;
            else if (SourceSuffixes.Contains(suffix)) role = Role.Source;
            else
            {
                // "perşembe dersini" kaynaktır; "cuma 17:00'ye" hedeftir; "salı 16:00 dersini"
                // yine kaynaktır - saatin arkasından ders geliyorsa gün o derse aittir.
                var next = NextMeaningfulIndex(tokens, i + 1);
                role = next is not { } n ? Role.Undecided
                    : tokens[n].StartsWith("ders", StringComparison.Ordinal) ? Role.Source
                    : TryReadTime(tokens, n, out _, out _) ? (FollowedByLesson(tokens, n) ? Role.Source : Role.Target)
                    : Role.Undecided;
            }

            found.Add((role, day));
        }

        return Assign(found);
    }

    private static (TimeOnly? Source, TimeOnly? Target) ExtractTimes(string[] tokens)
    {
        var found = new List<(Role Role, TimeOnly Time)>();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (!TryReadTime(tokens, i, out var time, out var role)) continue;

            if (role == Role.Undecided && FollowedByLesson(tokens, i)) role = Role.Source;

            found.Add((role, time));
        }

        return Assign(found);
    }

    // Açık rolü olmayan ifade: başka bir hedef varsa ya da arkasından aynı türden bir ifade
    // daha geliyorsa kaynaktır ("16:00 dersini 17:00'ye"), yoksa hedeftir ("cuma 17:00").
    private static (T? Source, T? Target) Assign<T>(List<(Role Role, T Value)> found) where T : struct
    {
        T? source = found.Where(f => f.Role == Role.Source).Select(f => (T?)f.Value).FirstOrDefault();
        T? target = found.Where(f => f.Role == Role.Target).Select(f => (T?)f.Value).FirstOrDefault();

        var undecided = found.Where(f => f.Role == Role.Undecided).Select(f => f.Value).ToList();
        for (var i = 0; i < undecided.Count; i++)
        {
            var laterExists = i + 1 < undecided.Count;
            if ((target is not null || laterExists) && source is null) source = undecided[i];
            else target ??= undecided[i];
        }

        return (source, target);
    }

    private static (DayReference? Source, DayReference? Target) Assign(List<(Role Role, DayReference Day)> found)
    {
        var source = found.Where(f => f.Role == Role.Source).Select(f => f.Day).FirstOrDefault();
        var target = found.Where(f => f.Role == Role.Target).Select(f => f.Day).FirstOrDefault();

        var undecided = found.Where(f => f.Role == Role.Undecided).Select(f => f.Day).ToList();
        for (var i = 0; i < undecided.Count; i++)
        {
            var laterExists = i + 1 < undecided.Count;
            if ((target is not null || laterExists) && source is null) source = undecided[i];
            else target ??= undecided[i];
        }

        return (source, target);
    }

    private static bool TryReadDay(string token, out DayReference day, out string suffix)
    {
        foreach (var (stem, weekday) in Weekdays)
        {
            if (!token.StartsWith(stem, StringComparison.Ordinal)) continue;
            suffix = token[stem.Length..];
            if (!IsDaySuffix(suffix)) break;
            day = new DayReference(weekday, null, false);
            return true;
        }

        foreach (var (stem, offset) in RelativeDays)
        {
            if (!token.StartsWith(stem, StringComparison.Ordinal)) continue;
            suffix = token[stem.Length..];
            if (!IsDaySuffix(suffix)) break;
            day = new DayReference(null, offset, false);
            return true;
        }

        day = null!;
        suffix = "";
        return false;
    }

    private static bool IsDaySuffix(string suffix) =>
        NeutralDaySuffixes.Contains(suffix) || TargetSuffixes.Contains(suffix) || SourceSuffixes.Contains(suffix);

    // Saatin (ve ayrı yazılmış ekinin) hemen arkasından "ders..." geliyor mu: "16:00 dersini".
    private static bool FollowedByLesson(string[] tokens, int timeIndex)
    {
        var next = timeIndex + 1;
        if (next < tokens.Length && TimeSuffix(tokens[next]) is not null) next++;
        return next < tokens.Length && tokens[next].StartsWith("ders", StringComparison.Ordinal);
    }

    private static int? NextMeaningfulIndex(string[] tokens, int from)
    {
        for (var i = from; i < tokens.Length; i++)
        {
            if (!DayFillers.Contains(tokens[i])) return i;
        }

        return null;
    }

    // "17:00", "17:00 ye", "17ye", "saat 17", "17 de", "16 daki", "saat 5" (→ 17:00).
    // Yalnız sayı ancak "saat" ile ya da bir ek ile birlikte saat sayılır - "2 telafi" saat değildir.
    private static bool TryReadTime(string[] tokens, int index, out TimeOnly time, out Role role)
    {
        time = default;
        role = Role.Undecided;
        if (index < 0 || index >= tokens.Length) return false;

        var match = TimeToken().Match(tokens[index]);
        if (!match.Success) return false;

        var hour = int.Parse(match.Groups["h"].Value);
        var minute = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value) : 0;
        var attachedSuffix = match.Groups["s"].Success ? match.Groups["s"].Value : null;
        var nextSuffix = index + 1 < tokens.Length ? TimeSuffix(tokens[index + 1]) : null;
        var afterSaat = index > 0 && tokens[index - 1] == "saat";

        var hasClockForm = match.Groups["m"].Success;
        if (!hasClockForm && attachedSuffix is null && nextSuffix is null && !afterSaat) return false;
        if (hour > 23 || minute > 59) return false;

        // Okul günü 09:00-21:00 (frontend SCHOOL_DAY_START/END): "saat 5" öğleden sonradır.
        var morning = (index > 0 && tokens[index - 1] == "sabah") || (index > 1 && tokens[index - 2] == "sabah");
        if (hour is >= 1 and <= 8 && !morning) hour += 12;

        time = new TimeOnly(hour, minute);
        role = attachedSuffix is not null ? SuffixRole(attachedSuffix) : nextSuffix ?? Role.Undecided;
        return true;
    }

    private static Role? TimeSuffix(string token) =>
        TargetSuffixes.Contains(token) ? Role.Target
        : SourceSuffixes.Contains(token) ? Role.Source
        : token is "da" or "de" or "ta" or "te" ? Role.Undecided
        : null;

    private static Role SuffixRole(string suffix) =>
        TargetSuffixes.Contains(suffix) ? Role.Target
        : SourceSuffixes.Contains(suffix) ? Role.Source
        : Role.Undecided;

    private static bool HasEveryWeekday(string[] tokens)
    {
        for (var i = 0; i + 1 < tokens.Length; i++)
        {
            if (tokens[i] == "her" && TryReadDay(tokens[i + 1], out var day, out _) && day.Weekday is not null) return true;
        }

        return false;
    }

    private static bool HasPrefix(string[] tokens, params string[] prefixes) =>
        tokens.Any(token => prefixes.Any(prefix => token.StartsWith(prefix, StringComparison.Ordinal)));

    private static bool HasAnyPhrase(string[] tokens, string[] phrases) => phrases.Any(phrase => HasPhrase(tokens, phrase));

    private static bool HasPhrase(string[] tokens, string phrase) =>
        $" {string.Join(' ', tokens)} ".Contains($" {phrase}", StringComparison.Ordinal);

    [GeneratedRegex(@"^(?<h>\d{1,2})(?::(?<m>\d{2}))?(?<s>ya|ye|a|e|da|de|ta|te|daki|deki|taki|teki|den|dan|ten|tan)?$")]
    private static partial Regex TimeToken();

    [GeneratedRegex(@"(ilir|inir|enir|ulur|unur|anir|irim|arim|erim|urum|iyorum)$")]
    private static partial Regex HowToVerb();
}
