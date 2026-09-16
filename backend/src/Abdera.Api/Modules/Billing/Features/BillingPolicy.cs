using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// İndirim politikası: çoklu kurs %, kardeş %, vade günü ve peşin ödeme kademeleri.
// Tek uç noktada toplandı çünkü hepsi aynı ekranda birlikte düzenleniyor ve birlikte
// anlam taşıyor - ayrı ayrı kaydedilmeleri yarım kalmış bir politika bırakabilirdi.
public static class BillingPolicy
{
    public record TierRequest(int MinMonths, decimal Percent);
    public record UpdateRequest(
        decimal MultiCourseDiscountPercent,
        decimal SiblingDiscountPercent,
        int DueDayOfMonth,
        List<TierRequest> PrepayTiers);

    public record TierResponse(Guid Id, int MinMonths, decimal Percent);
    public record PolicyResponse(
        decimal MultiCourseDiscountPercent,
        decimal SiblingDiscountPercent,
        int DueDayOfMonth,
        List<TierResponse> PrepayTiers);

    public static void MapBillingPolicy(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/billing-policy").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", GetAsync);
        group.MapPut("", UpdateAsync);
    }

    private static async Task<IResult> GetAsync(AbderaDbContext db)
    {
        var settings = await BillingSettings.GetCurrentAsync(db);
        var tiers = await db.PrepayDiscountTiers.AsNoTracking().OrderBy(tier => tier.MinMonths).ToListAsync();
        return Results.Ok(ToResponse(settings, tiers));
    }

    private static async Task<IResult> UpdateAsync(
        UpdateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var duplicateMonths = request.PrepayTiers
            .GroupBy(tier => tier.MinMonths)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateMonths.Count > 0)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["prepayTiers"] = [$"Aynı ay sayısı için birden fazla kademe tanımlanamaz: {string.Join(", ", duplicateMonths)}."],
            });

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);

        var settings = await db.BillingSettings.SingleOrDefaultAsync(s => s.Id == BillingSettings.SingletonId);
        var before = settings is null ? null : JsonSerializer.Serialize(new
        {
            multiCourse = settings.MultiCourseDiscountPercent,
            sibling = settings.SiblingDiscountPercent,
            dueDay = settings.DueDayOfMonth,
        });

        if (settings is null)
        {
            settings = BillingSettings.CreateDefault(now);
            db.BillingSettings.Add(settings);
        }

        settings.Update(
            request.MultiCourseDiscountPercent, request.SiblingDiscountPercent,
            request.DueDayOfMonth, actorId, now);

        // Kademeler tam değişimle yazılır (sil + ekle): ekranda kullanıcı kademe silebiliyor,
        // kısmi güncelleme "hangisi kaldırıldı" sorusunu istemciye yıkardı. Tarifenin
        // kendisi gibi mali bir kayıt değil, bir POLİTİKA - geçmiş aidatlar zaten
        // hesaplanmış tutarlarını taşıyor, kademe silmek onları etkilemiyor.
        db.PrepayDiscountTiers.RemoveRange(await db.PrepayDiscountTiers.ToListAsync());
        foreach (var tier in request.PrepayTiers.OrderBy(tier => tier.MinMonths))
        {
            db.PrepayDiscountTiers.Add(PrepayDiscountTier.Create(tier.MinMonths, tier.Percent));
        }

        db.AuditLogs.Add(AuditLog.Record(
            actorId, "billing_policy.updated", nameof(BillingSettings), settings.Id, now,
            beforeJson: before,
            afterJson: JsonSerializer.Serialize(new
            {
                multiCourse = request.MultiCourseDiscountPercent,
                sibling = request.SiblingDiscountPercent,
                dueDay = request.DueDayOfMonth,
                tiers = request.PrepayTiers.Select(tier => new { tier.MinMonths, tier.Percent }),
            })));

        await db.SaveChangesAsync();

        var saved = await db.PrepayDiscountTiers.AsNoTracking().OrderBy(tier => tier.MinMonths).ToListAsync();
        return Results.Ok(ToResponse(settings, saved));
    }

    private static PolicyResponse ToResponse(BillingSettings settings, List<PrepayDiscountTier> tiers) => new(
        settings.MultiCourseDiscountPercent,
        settings.SiblingDiscountPercent,
        settings.DueDayOfMonth,
        tiers.Select(tier => new TierResponse(tier.Id, tier.MinMonths, tier.Percent)).ToList());
}
