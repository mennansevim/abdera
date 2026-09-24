using Abdera.Api.Modules.Progress.Domain;

namespace Abdera.Api.Modules.Progress.Infrastructure;

// Ai__Provider tanımsız/Disabled (varsayılan) - okul bir AI sağlayıcısı yapılandırmamış.
//
// Özelliğin kapalı olması bir hata değildir: gelişim ekranı notlar, ödevler ve repertuvarla
// eksiksiz çalışır; yalnızca "Genel gelişim" yorumu gösterilmez.
public class DisabledProgressSummaryGenerator : IProgressSummaryGenerator
{
    public bool IsAvailable => false;

    public string ModelName => "disabled";

    public Task<ProgressSummaryResult> GenerateAsync(
        ProgressSummaryRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProgressSummaryResult(
            false,
            null,
            "Gelişim yorumu kapalı: okul için bir AI sağlayıcısı yapılandırılmamış (Ai__Provider)."));
}
