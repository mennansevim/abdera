using Abdera.Api.Modules.Profitability.Domain;

namespace Abdera.Api.Modules.Profitability.Infrastructure;

// Ai__Provider tanımsız/Disabled: kârlılık ekranı yorum kartı olmadan eksiksiz çalışır.
public class DisabledProfitCommentaryGenerator : IProfitCommentaryGenerator
{
    public bool IsAvailable => false;

    public string ModelName => "disabled";

    public Task<ProfitCommentaryResult> GenerateAsync(string facts, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProfitCommentaryResult(false, null, "Kârlılık yorumu kapalı: AI sağlayıcısı yapılandırılmamış (Ai__Provider)."));
}
