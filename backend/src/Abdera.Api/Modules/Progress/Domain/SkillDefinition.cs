namespace Abdera.Api.Modules.Progress.Domain;

public class SkillDefinition
{
    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Label { get; private set; } = null!;
    public Guid? InstrumentId { get; private set; }

    private SkillDefinition() { }

    public static SkillDefinition Create(string code, string label, Guid? instrumentId = null)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("Yetenek kodu boş olamaz.", nameof(code));
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Yetenek adı boş olamaz.", nameof(label));

        return new SkillDefinition
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToUpperInvariant(),
            Label = label.Trim(),
            InstrumentId = instrumentId,
        };
    }
}
