namespace Abdera.Api.Shared;

// ARC-3 (docs/13-audit-fix-prompt.md): Notifications ve BankTransactions listeleri Take(200)
// ile sessizce kesiliyordu - kullanıcıya "daha fazlası var" sinyali yoktu, toplam sayı
// dönmüyordu. Bu ortak zarf, sayfalanan tüm liste uç noktalarında aynı şekli kullanır.
public record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public static class Pagination
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    // page/pageSize sorgu parametrelerini güvenli sınırlara oturtur - negatif/sıfır/aşırı
    // büyük değerler sessizce clamp'lenir, 400 döndürmez (liste uç noktaları için yeterli).
    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
