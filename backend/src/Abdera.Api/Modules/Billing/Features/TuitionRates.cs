using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// Okulun ücret tarifesi - eski /api/price-lists + /api/enrollments/{id}/fee-plan ikilisinin
// yerini alır. Bugün iki satır: Birebir 4 ders 6.000 TL, Grup 4 ders 4.500 TL.
//
// Zam ayrı bir "toplu güncelleme" işlemi değil (eski Pricing/BulkUpdate.cs): yeni bir
// yürürlük tarihiyle yeni satır açılır, öncekisi otomatik kapanır. Geçmiş aidatlar
// tutarını kendi satırına kopyaladığı için değişmez (docs/10-decisions.md A1).
public static class TuitionRates
{
    public record CreateRequest(CourseKind CourseKind, int LessonsPerMonth, decimal MonthlyAmount, DateOnly EffectiveFrom, string? Currency);
    public record TuitionRateResponse(
        Guid Id, CourseKind CourseKind, int LessonsPerMonth, decimal MonthlyAmount, string Currency,
        DateOnly EffectiveFrom, DateOnly? EffectiveUntil, bool IsCurrent);

    public static void MapTuitionRates(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tuition-rates").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db, IClock clock)
    {
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var rates = await db.TuitionRates.AsNoTracking()
            .OrderBy(rate => rate.CourseKind)
            .ThenByDescending(rate => rate.EffectiveFrom)
            .ToListAsync();

        return Results.Ok(rates.Select(rate => ToResponse(rate, today)).ToList());
    }

    private static async Task<IResult> CreateAsync(
        CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        if (request.MonthlyAmount < 0)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["monthlyAmount"] = ["Tutar negatif olamaz."],
            });
        if (request.LessonsPerMonth is < 1 or > 31)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["lessonsPerMonth"] = ["Aylık ders sayısı 1 ile 31 arasında olmalı."],
            });

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);

        // Aynı ders türünün açık uçlu tarifesi varsa yeni tarifenin BİR GÜN ÖNCESİNDE
        // kapatılır - böylece hiçbir gün iki tarifeye birden düşmez ve DB'deki
        // "tür başına tek açık tarife" unique index'i ihlal edilmez.
        var open = await db.TuitionRates
            .Where(rate => rate.CourseKind == request.CourseKind && rate.EffectiveUntil == null)
            .SingleOrDefaultAsync();
        var earliestStart = await db.TuitionRates
            .Where(rate => rate.CourseKind == request.CourseKind)
            .OrderBy(rate => rate.EffectiveFrom)
            .Select(rate => (DateOnly?)rate.EffectiveFrom)
            .FirstOrDefaultAsync();

        // En eski tarifeden ÖNCE başlayan tarife: geçmiş bir dönemi kapsamak için (örn. tohum
        // tarifesi 1 Eylül'de başlıyor ama Ağustos'un aidatı da alınacak - "2026-08: Birebir
        // dersi için geçerli ücret tarifesi yok"). Yeni satır en eski tarifenin bir gün
        // öncesinde kapanır; hiçbir gün iki tarifeye düşmez ve açık uçlu tarife değişmez.
        // Mevcut tarifeler arasına (geçmişin ortasına) giren bir tarih hâlâ reddedilir.
        var backfill = earliestStart is { } earliest && request.EffectiveFrom < earliest;

        if (open is not null && !backfill)
        {
            if (request.EffectiveFrom <= open.EffectiveFrom)
                throw new ConflictException(
                    $"Yürürlükteki tarife {open.EffectiveFrom:yyyy-MM-dd} tarihinde başlıyor. Yeni tarife bu tarihten sonra " +
                    $"başlamalı ya da ilk tarifeden ({earliestStart:yyyy-MM-dd}) önceki bir dönemi kapsamalı.");

            open.EndOn(request.EffectiveFrom.AddDays(-1));
        }

        var rate = TuitionRate.Create(
            request.CourseKind, request.LessonsPerMonth, request.MonthlyAmount,
            request.Currency ?? "TRY", request.EffectiveFrom, actorId, now);
        if (backfill) rate.EndOn(earliestStart!.Value.AddDays(-1));

        db.TuitionRates.Add(rate);
        db.AuditLogs.Add(AuditLog.Record(
            actorId, "tuition_rate.created", nameof(TuitionRate), rate.Id, now,
            beforeJson: open is null || backfill ? null : JsonSerializer.Serialize(new
            {
                courseKind = open.CourseKind.ToString(),
                monthlyAmount = open.MonthlyAmount,
                effectiveFrom = open.EffectiveFrom,
                effectiveUntil = open.EffectiveUntil,
            }),
            afterJson: JsonSerializer.Serialize(new
            {
                courseKind = rate.CourseKind.ToString(),
                lessonsPerMonth = rate.LessonsPerMonth,
                monthlyAmount = rate.MonthlyAmount,
                currency = rate.Currency,
                effectiveFrom = rate.EffectiveFrom,
                effectiveUntil = rate.EffectiveUntil,
            })));

        await db.SaveChangesAsync();

        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);
        return Results.Created($"/api/tuition-rates/{rate.Id}", ToResponse(rate, today));
    }

    private static TuitionRateResponse ToResponse(TuitionRate rate, DateOnly today) => new(
        rate.Id, rate.CourseKind, rate.LessonsPerMonth, rate.MonthlyAmount, rate.Currency,
        rate.EffectiveFrom, rate.EffectiveUntil, rate.IsActiveOn(today));
}
