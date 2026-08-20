using System.Security.Claims;
using Abdera.Api.Modules.Pricing.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Pricing.Features;

// docs/07-api.md GET/POST /api/price-lists. docs/04-permissions.md: fiyat listesi yalnızca
// Admin. Bir liste tüm kalemleriyle (enstrüman x süre x tip) birlikte tek seferde oluşturulur -
// gerçek kullanım zaten böyle: yeni sezonun tüm fiyatları bir arada belirlenir.
public static class PriceLists
{
    public record CreateItemRequest(Guid InstrumentId, int DurationMinutes, BillingType BillingType, decimal Amount, string? Currency, int? PackageLessonCount);
    public record CreateRequest(string Name, DateOnly EffectiveFrom, DateOnly? EffectiveUntil, List<CreateItemRequest> Items);

    public record ItemResponse(Guid Id, Guid InstrumentId, int DurationMinutes, BillingType BillingType, decimal Amount, string Currency, int? PackageLessonCount);
    public record PriceListResponse(Guid Id, string Name, DateOnly EffectiveFrom, DateOnly? EffectiveUntil, List<ItemResponse> Items);

    public static void MapPriceLists(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/price-lists").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db)
    {
        var lists = await db.PriceLists.OrderByDescending(p => p.EffectiveFrom).ToListAsync();
        var items = await db.PriceListItems.ToListAsync();

        return Results.Ok(lists.Select(l => ToResponse(l, items.Where(i => i.PriceListId == l.Id))));
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        if (request.Items.Count == 0)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["items"] = ["En az bir fiyat kalemi gerekli."] });

        var priceList = PriceList.Create(request.Name, request.EffectiveFrom, request.EffectiveUntil, AuthContext.GetUserId(principal), clock.UtcNow);

        var items = new List<PriceListItem>();
        foreach (var itemRequest in request.Items)
        {
            if (!await db.Instruments.AnyAsync(i => i.Id == itemRequest.InstrumentId))
                throw new NotFoundException($"Enstrüman bulunamadı: {itemRequest.InstrumentId}");

            await EnsureNoOverlapAsync(itemRequest.InstrumentId, itemRequest.DurationMinutes, itemRequest.BillingType,
                request.EffectiveFrom, request.EffectiveUntil, db);

            items.Add(PriceListItem.Create(
                priceList.Id, itemRequest.InstrumentId, itemRequest.DurationMinutes, itemRequest.BillingType,
                itemRequest.Amount, itemRequest.Currency ?? "TRY", itemRequest.PackageLessonCount));
        }

        db.PriceLists.Add(priceList);
        db.PriceListItems.AddRange(items);
        await db.SaveChangesAsync();

        return Results.Created($"/api/price-lists/{priceList.Id}", ToResponse(priceList, items));
    }

    // docs/03-erd.md notu: "Aynı (instrument_id, duration_minutes, billing_type) için
    // çakışan tarih aralığı olamaz" - aralık üst PriceList'ten miras alınır.
    private static async Task EnsureNoOverlapAsync(
        Guid instrumentId, int durationMinutes, BillingType billingType,
        DateOnly newFrom, DateOnly? newUntil, AbderaDbContext db)
    {
        var candidates = await db.PriceListItems
            .Where(i => i.InstrumentId == instrumentId && i.DurationMinutes == durationMinutes && i.BillingType == billingType)
            .Join(db.PriceLists, i => i.PriceListId, l => l.Id, (i, l) => new { l.EffectiveFrom, l.EffectiveUntil, l.Name })
            .ToListAsync();

        var newUntilOrMax = newUntil ?? DateOnly.MaxValue;
        foreach (var candidate in candidates)
        {
            var candidateUntilOrMax = candidate.EffectiveUntil ?? DateOnly.MaxValue;
            var overlaps = newFrom <= candidateUntilOrMax && candidate.EffectiveFrom <= newUntilOrMax;
            if (overlaps)
            {
                throw new ConflictException(
                    $"Bu enstrüman/süre/tip kombinasyonu '{candidate.Name}' fiyat listesiyle tarih olarak çakışıyor.");
            }
        }
    }

    private static PriceListResponse ToResponse(PriceList list, IEnumerable<PriceListItem> items) => new(
        list.Id, list.Name, list.EffectiveFrom, list.EffectiveUntil,
        items.Select(i => new ItemResponse(i.Id, i.InstrumentId, i.DurationMinutes, i.BillingType, i.Amount, i.Currency, i.PackageLessonCount)).ToList());
}
