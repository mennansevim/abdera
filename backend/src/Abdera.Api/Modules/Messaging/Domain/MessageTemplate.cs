namespace Abdera.Api.Modules.Messaging.Domain;

// docs/03-erd.md - Messaging > message_templates. docs/06-whatsapp.md: gövde Meta'ya
// onaylatılan metnin ta kendisi - burada placeholder'larla (`{{guardian_name}}` gibi) saklanır.
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
