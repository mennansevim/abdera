using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Profitability.Domain;

// Kârlılık ekranının aylık yapay zekâ yorumu (docs/10-decisions.md V3). Ay başına tek satır:
// ekran o ay ilk açıldığında üretilir, sonra kayıttan gösterilir - token tüketimi ayda bir
// çağrı + en fazla MaxRefreshesPerMonth elle yenileme ile sınırlı.
public class ProfitCommentary
{
    public const int MaxRefreshesPerMonth = 3;
    public const int TextMaxLength = 2000;

    public Guid Id { get; private set; }
    // "2026-10" - okulun yerel takvimindeki ay.
    public string Period { get; private set; } = null!;
    public string Text { get; private set; } = null!;
    public string Model { get; private set; } = null!;
    public int RefreshCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ProfitCommentary() { }

    public static ProfitCommentary Create(string period, string text, string model, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Period = period,
        Text = Clip(text),
        Model = model,
        RefreshCount = 0,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public bool CanRefresh => RefreshCount < MaxRefreshesPerMonth;

    public void Refresh(string text, string model, DateTimeOffset now)
    {
        if (!CanRefresh)
            throw new ConflictException($"Bu ayın yorumu en fazla {MaxRefreshesPerMonth} kez yenilenebilir; hak doldu.");
        Text = Clip(text);
        Model = model;
        RefreshCount++;
        UpdatedAt = now;
    }

    private static string Clip(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length > TextMaxLength ? trimmed[..TextMaxLength] : trimmed;
    }
}

// Ai__Provider ile seçilir (IProgressSummaryGenerator ile aynı yapılandırma ve aynı desen).
public interface IProfitCommentaryGenerator
{
    bool IsAvailable { get; }
    string ModelName { get; }

    // facts: ProfitCommentaryFacts.Build'in ürettiği kısa, isimsiz özet metin.
    Task<ProfitCommentaryResult> GenerateAsync(string facts, CancellationToken cancellationToken = default);
}

public record ProfitCommentaryResult(bool Success, string? Text, string? Error);
