using System.Text.RegularExpressions;

namespace Abdera.Api.Modules.Messaging.Domain;

// docs/03-erd.md - Messaging > message_templates. docs/06-whatsapp.md: gövde Meta'ya
// onaylatılan metnin ta kendisi - burada placeholder'larla (`{{guardian_name}}` gibi) saklanır.
// Panelden düzenlenemez (docs/10-decisions.md T1): Meta şablonu sabit, gövdeyi burada
// değiştirmek yalnızca değişken sırasını onaylı şablondan kaydırırdı. Değişiklik migration'la.
public class MessageTemplate
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Language { get; private set; } = "tr";
    public string Body { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    private MessageTemplate() { }

    public static MessageTemplate Create(string name, string body, string language = "tr") => new()
    {
        Id = Guid.NewGuid(),
        Name = name.Trim(),
        Language = language,
        Body = body,
        IsActive = true,
    };

    private static readonly Regex Placeholder = new(@"\{\{([A-Za-z0-9_]+)\}\}", RegexOptions.Compiled);

    // Meta Cloud API şablon değişkenleri konumsaldır ({{1}}, {{2}}...) ve sayısı onaylı şablonla
    // birebir tutmalıdır, yoksa mesaj reddedilir. Mesaj oluşturucu bir bildirim türü için
    // gerekebilecek bütün değerleri üretir (ör. ders mesajlarında lesson_time + new_lesson_time);
    // gönderilecek olanlar ve sıraları bu gövdedeki yer tutucuların ilk geçiş sırasıdır. Böylece
    // Meta'ya yazılan şablonun {{n}}'i, panelde görünen gövdenin n. değişkeniyle aynı olur.
    // Gövdede geçip değeri üretilmeyen adlar `missing`'e yazılır (şablon ile kod uyuşmuyor).
    public IReadOnlyDictionary<string, string> OrderParameters(
        IReadOnlyDictionary<string, string> values, out IReadOnlyList<string> missing)
    {
        var ordered = new Dictionary<string, string>();
        var missingNames = new List<string>();
        foreach (Match match in Placeholder.Matches(Body))
        {
            var name = match.Groups[1].Value;
            if (ordered.ContainsKey(name) || missingNames.Contains(name)) continue;
            if (values.TryGetValue(name, out var value)) ordered[name] = value;
            else missingNames.Add(name);
        }
        missing = missingNames;
        return ordered;
    }

    // {{key}} placeholder'larını verilen değerlerle değiştirir - basit, regex'siz.
    public string Render(IReadOnlyDictionary<string, string> parameters)
    {
        var result = Body;
        foreach (var (key, value) in parameters)
        {
            result = result.Replace($"{{{{{key}}}}}", value);
        }
        return result;
    }
}
