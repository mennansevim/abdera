using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Pricing.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// docs/07-api.md'de açık bir /fee-plan uç noktası yoktu ama master prompt'un "Payment" iş
// akışı "Create fee plan" ile başlıyor - Enrollment altında iç içe (nested resource), People'ın
// enrollment/guardian ekleri gibi (docs/10-decisions.md).
public static class FeePlans
{
    public record CreateRequest(Guid PriceListItemId, int? DueDay, DateOnly ActiveFrom);
    public record FeePlanResponse(
        Guid Id, Guid EnrollmentId, BillingType BillingType, decimal Amount, string Currency,
        int? DueDay, int? PackageLessonCount, DateOnly ActiveFrom, DateOnly? ActiveUntil);

    public static void MapFeePlans(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/enrollments/{enrollmentId:guid}/fee-plan", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapGet("/api/enrollments/{enrollmentId:guid}/fee-plan", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> CreateAsync(Guid enrollmentId, CreateRequest request, AbderaDbContext db, IClock clock)
    {
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(e => e.Id == enrollmentId)
            ?? throw new NotFoundException("Kayıt (enrollment) bulunamadı.");

        var item = await db.PriceListItems.SingleOrDefaultAsync(i => i.Id == request.PriceListItemId)
            ?? throw new NotFoundException("Fiyat kalemi bulunamadı.");

        if (item.InstrumentId != enrollment.InstrumentId)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["priceListItemId"] = ["Seçilen fiyat kalemi bu kaydın enstrümanıyla uyuşmuyor."],
            });

        var hasActivePlan = await db.FeePlans.AnyAsync(f => f.EnrollmentId == enrollmentId && f.ActiveUntil == null);
        if (hasActivePlan)
            throw new ConflictException("Bu kayıt için zaten aktif bir ücret planı var.");

        var feePlan = FeePlan.CreateFromPriceListItem(enrollmentId, item, request.DueDay, request.ActiveFrom, clock.UtcNow);
        db.FeePlans.Add(feePlan);
        await db.SaveChangesAsync();

        return Results.Created($"/api/enrollments/{enrollmentId}/fee-plan", ToResponse(feePlan));
    }

    private static async Task<IResult> GetAsync(Guid enrollmentId, AbderaDbContext db)
    {
        var feePlan = await db.FeePlans
            .Where(f => f.EnrollmentId == enrollmentId && f.ActiveUntil == null)
            .SingleOrDefaultAsync();

        return feePlan is null ? Results.NotFound() : Results.Ok(ToResponse(feePlan));
    }

    private static FeePlanResponse ToResponse(FeePlan f) => new(
        f.Id, f.EnrollmentId, f.BillingType, f.Amount, f.Currency, f.DueDay, f.PackageLessonCount, f.ActiveFrom, f.ActiveUntil);
}
