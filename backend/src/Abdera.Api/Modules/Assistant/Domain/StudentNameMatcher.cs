namespace Abdera.Api.Modules.Assistant.Domain;

// Metinde geçen öğrenci adını bulur. Adın son kelimesi Türkçe ek alabilir ("Mennan'ın",
// "Ece'ye", kesmesiz "Ecenin"). Ad + soyad eşleşmesi tek başına addan, tek başına ad da
// tek başına soyaddan güçlüdür; yalnızca en güçlü düzeydeki eşleşmeler döner. Aynı düzeyde
// birden çok öğrenci kalırsa seçim kullanıcıya bırakılır - asistan tahmin etmez.
public static class StudentNameMatcher
{
    public record Candidate(Guid StudentId, string FirstName, string LastName);

    public record Result(IReadOnlyList<Guid> StudentIds, IReadOnlySet<int> MatchedTokenIndexes);

    private enum Strength { None, LastName, FirstName, FullName }

    // Tek başına ad/soyad için en kısa uzunluk: "Su", "Ay" gibi adlar sıradan kelimelerle
    // karışır; bunlar yalnızca ad + soyad birlikte yazıldığında eşleşir.
    private const int MinimumSingleNameLength = 3;

    private static readonly HashSet<string> Suffixes =
    [
        "in", "un", "nin", "nun",
        "a", "e", "ya", "ye", "na", "ne",
        "i", "u", "yi", "yu", "ni", "nu",
        "la", "le", "yla", "yle",
        "da", "de", "ta", "te", "nda", "nde",
        "dan", "den", "tan", "ten", "ndan", "nden",
    ];

    public static Result Match(IReadOnlyList<string> tokens, IEnumerable<Candidate> candidates)
    {
        var best = Strength.None;
        var studentIds = new List<Guid>();
        var matchedIndexes = new HashSet<int>();

        foreach (var candidate in candidates)
        {
            var first = AssistantText.Tokens(AssistantText.Normalize(candidate.FirstName));
            var last = AssistantText.Tokens(AssistantText.Normalize(candidate.LastName));
            if (first.Length == 0) continue;

            var (strength, indexes) = Score(tokens, first, last);
            if (strength == Strength.None || strength < best) continue;
            if (strength > best)
            {
                best = strength;
                studentIds.Clear();
                matchedIndexes.Clear();
            }

            studentIds.Add(candidate.StudentId);
            matchedIndexes.UnionWith(indexes);
        }

        return new Result(studentIds, matchedIndexes);
    }

    private static (Strength, IReadOnlyList<int>) Score(IReadOnlyList<string> tokens, string[] first, string[] last)
    {
        if (last.Length > 0 && Find(tokens, [.. first, .. last]) is { } full) return (Strength.FullName, full);
        if (string.Concat(first).Length >= MinimumSingleNameLength && Find(tokens, first) is { } firstOnly) return (Strength.FirstName, firstOnly);
        if (string.Concat(last).Length >= MinimumSingleNameLength && Find(tokens, last) is { } lastOnly) return (Strength.LastName, lastOnly);
        return (Strength.None, []);
    }

    // Kelimeler art arda gelmeli; son kelime ek alabilir, öncekiler tam eşleşmeli.
    private static IReadOnlyList<int>? Find(IReadOnlyList<string> tokens, string[] words)
    {
        for (var start = 0; start + words.Length <= tokens.Count; start++)
        {
            var matches = true;
            for (var offset = 0; offset < words.Length && matches; offset++)
            {
                var token = tokens[start + offset];
                var word = words[offset];
                matches = offset == words.Length - 1 ? IsWordWithSuffix(token, word) : token == word;
            }

            if (!matches) continue;

            // Kesmeli yazımda ek ayrı kelimedir ("mennan in"); o da adın parçası sayılır.
            var indexes = Enumerable.Range(start, words.Length).ToList();
            var next = start + words.Length;
            if (next < tokens.Count && tokens[start + words.Length - 1] == words[^1] && Suffixes.Contains(tokens[next])) indexes.Add(next);
            return indexes;
        }

        return null;
    }

    private static bool IsWordWithSuffix(string token, string word) =>
        token == word || (token.StartsWith(word, StringComparison.Ordinal) && Suffixes.Contains(token[word.Length..]));
}
