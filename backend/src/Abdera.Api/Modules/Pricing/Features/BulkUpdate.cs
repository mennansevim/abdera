using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Pricing.Features;

// docs/10-decisions.md A1: "Toplu zam işlemi önizlemeli olmalı (kimden ne kadar alınacak,
// uygulamadan önce göster) ve audit'e yazmalı." Geçmiş Receivable'lar snapshot aldığı için
// bu işlem yalnızca FUTURE tahsilatları etkiler - activeFeePlanCount bunu görünür kılar.
public static class BulkUpdate
{
    public record Request(decimal PercentageChange);

    public record ItemPreview(
        Guid ItemId, string InstrumentName, int DurationMinutes, string BillingType,
        decimal OldAmount, decimal NewAmount, int ActiveFeePlanCount);

    public static void MapBulkUpdate(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/price-lists/{priceListId:guid}/preview-bulk-update", PreviewAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost("/api/price-lists/{priceListId:guid}/apply", ApplyAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> PreviewAsync(Guid priceListId, Request request, AbderaDbContext db) =>
        Results.Ok(await BuildPreviewAsync(priceListId, request.PercentageChange, db));

    private static async Task<IResult> ApplyAsync(
        Guid priceListId, Request request, System.Security.Claims.ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var preview = await BuildPreviewAsync(priceListId, request.PercentageChange, db);

        var items = await db.PriceListItems.Where(i => i.PriceListId == priceListId).ToListAsync();
        foreach (var item in items)
        {
            var before = item.Amount;
            item.ApplyPercentageChange(request.PercentageChange);

            // JsonSerializer kullanılır - string interpolation ile decimal basmak makinenin
            // kültürüne (örn. tr-TR'de "1200,00") bağımlı geçersiz JSON üretebilir ve
            // jsonb kolonunda DbUpdateException'a yol açar - gerçek prod riski, canlıda
            // Türkçe yapılandırılmış bir sunucuda aynen tekrarlanırdı.
            db.AuditLogs.Add(AuditLog.Record(
                AuthContext.GetUserId(principal), "price_list_item.bulk_update",
                nameof(Domain.PriceListItem), item.Id, clock.UtcNow,
                beforeJson: System.Text.Json.JsonSerializer.Serialize(new { amount = before }),
                afterJson: System.Text.Json.JsonSerializer.Serialize(new { amount = item.Amount })));
        }

        await db.SaveChangesAsync();
        return Results.Ok(preview);
    }

    private static async Task<List<ItemPreview>> BuildPreviewAsync(Guid priceListId, decimal percentageChange, AbderaDbContext db)
    {
        if (!await db.PriceLists.AnyAsync(l => l.Id == priceListId))
            throw new NotFoundException("Fiyat listesi bulunamadı.");

        var items = await db.PriceListItems
            .Where(i => i.PriceListId == priceListId)
            .Join(db.Instruments, i => i.InstrumentId, ins => ins.Id, (i, ins) => new { Item = i, ins.Name })
            .ToListAsync();

        // ARC-5 (docs/13-audit-fix-prompt.md): kalem başına ayrı bir CountAsync yerine
        // döngü öncesi tek bir GroupBy sorgusuyla tüm sayılar toplanıyor.
        var itemIds = items.Select(x => x.Item.Id).ToList();
        var activeFeePlanCounts = await db.FeePlans
            .Where(f => itemIds.Contains(f.PriceListItemId) && f.ActiveUntil == null)
            .GroupBy(f => f.PriceListItemId)
            .Select(g => new { PriceListItemId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PriceListItemId, g => g.Count);

        var result = new List<ItemPreview>();
        foreach (var x in items)
        {
            var newAmount = Math.Round(x.Item.Amount * (1 + percentageChange / 100m), 2, MidpointRounding.AwayFromZero);
            var activeFeePlanCount = activeFeePlanCounts.GetValueOrDefault(x.Item.Id);

            result.Add(new ItemPreview(
                x.Item.Id, x.Name, x.Item.DurationMinutes, x.Item.BillingType.ToString(),
                x.Item.Amount, newAmount, activeFeePlanCount));
        }

        return result;
    }
}
