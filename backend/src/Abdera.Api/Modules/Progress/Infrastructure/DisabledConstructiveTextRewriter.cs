using Abdera.Api.Modules.Progress.Domain;

namespace Abdera.Api.Modules.Progress.Infrastructure;

// Ai__Provider tanımsız/Disabled (varsayılan) - okul bir AI sağlayıcısı yapılandırmamış.
//
// Özelliğin kapalı olması bir hata değildir: öğretmen veli yorumunu elle yazar, onaylar ve
// geri çeker; gelişim akışının tamamı AI olmadan eksiksiz çalışır. Frontend bu durumu
// /api/auth/me üzerindeki aiRewriteAvailable bayrağından öğrenip butonu kapatır, böylece
// kullanıcı çalışmayan bir düğmeye basmaz.
public class DisabledConstructiveTextRewriter : IConstructiveTextRewriter
{
    public bool IsAvailable => false;

    public Task<ConstructiveRewriteResult> RewriteAsync(
        ConstructiveRewriteRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ConstructiveRewriteResult(
            false,
            null,
            "Yapıcı metne dönüştürme kapalı: okul için bir AI sağlayıcısı yapılandırılmamış (Ai__Provider)."));
}
