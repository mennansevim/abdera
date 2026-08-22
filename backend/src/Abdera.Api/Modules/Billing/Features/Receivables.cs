using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Pricing.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// docs/07-api.md GET/POST /api/receivables. docs/04-permissions.md: aidat/tahsilat
// tamamen Admin - Teacher'ın hiçbir mali veriye erişimi yok, scoping gerekmez.
public static class Receivables
{
    public record CreateRequest(Guid EnrollmentId, string Period);
    public record PaymentSummary(Guid Id, decimal Amount, DateOnly PaymentDate, PaymentMethod Method, string? Reference, string? Note);
    public record ReceivableResponse(
        Guid Id, Guid EnrollmentId, string Period, decimal Amount, string Currency,
        DateOnly DueDate, ReceivableStatus Status, decimal TotalPaid, List<PaymentSummary> Payments);

    public static void MapReceivables(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/receivables").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPost("/{receivableId:guid}/cancel", CancelAsync);
    }

    private static async Task<IResult> ListAsync(ReceivableStatus? status, AbderaDbContext db)
    {
        var query = db.Receivables.AsQueryable();
        if (status is { } s) query = query.Where(r => r.Status == s);

        var receivables = await query.OrderByDescending(r => r.DueDate).ToListAsync();
        var totals = await ComputeTotalsPaidAsync(receivables.Select(r => r.Id), db);
        var payments = await ComputePaymentsAsync(receivables.Select(r => r.Id), db);

        return Results.Ok(receivables.Select(r => ToResponse(r, totals.GetValueOrDefault(r.Id), payments.GetValueOrDefault(r.Id) ?? [])));
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, AbderaDbContext db, IClock clock)
    {
        if (await db.Receivables.AnyAsync(r => r.EnrollmentId == request.EnrollmentId && r.Period == request.Period))
            throw new ConflictException($"'{request.Period}' dönemi için bu kayda ait bir aidat zaten var.");

        var feePlan = await db.FeePlans
            .SingleOrDefaultAsync(f => f.EnrollmentId == request.EnrollmentId && f.ActiveUntil == null)
            ?? throw new NotFoundException("Bu kayıt için aktif bir ücret planı bulunamadı.");

        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var dueDate = ComputeDueDate(feePlan, request.Period, today);

        var receivable = Receivable.Create(
            request.EnrollmentId, feePlan.Id, feePlan.PriceListItemId, request.Period,
            feePlan.Amount, feePlan.Currency, dueDate, clock.UtcNow);

        db.Receivables.Add(receivable);
        await db.SaveChangesAsync();

        return Results.Created($"/api/receivables/{receivable.Id}", ToResponse(receivable, 0, []));
    }

    private static async Task<IResult> CancelAsync(Guid receivableId, AbderaDbContext db, IClock clock)
    {
        var receivable = await db.Receivables.SingleOrDefaultAsync(r => r.Id == receivableId)
            ?? throw new NotFoundException("Aidat bulunamadı.");

        receivable.Cancel(clock.UtcNow);
        await db.SaveChangesAsync();

        var totalPaid = await db.Payments.Where(p => p.ReceivableId == receivableId).SumAsync(p => (decimal?)p.Amount) ?? 0;
        var payments = await ComputePaymentsAsync([receivableId], db);
        return Results.Ok(ToResponse(receivable, totalPaid, payments.GetValueOrDefault(receivableId) ?? []));
    }

    // docs/03-erd.md period: "2026-09 gibi dönem etiketi". MONTHLY için ayın FeePlan.DueDay'i,
    // PACKAGE için hemen (bugün) vade tarihi kullanılır - paket önceden ödenir.
    private static DateOnly ComputeDueDate(FeePlan feePlan, string period, DateOnly today)
    {
        if (feePlan.BillingType == BillingType.Package)
        {
            return today;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(period, @"^\d{4}-(0[1-9]|1[0-2])$"))
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["period"] = ["Aylık aidat için dönem 'yyyy-MM' biçiminde olmalı (örn. 2026-09)."],
            });

        var year = int.Parse(period[..4]);
        var month = int.Parse(period[5..]);
        return new DateOnly(year, month, feePlan.DueDay ?? 1);
    }

    internal static async Task<Dictionary<Guid, decimal>> ComputeTotalsPaidAsync(IEnumerable<Guid> receivableIds, AbderaDbContext db)
    {
        var ids = receivableIds.ToList();
        return await db.Payments
            .Where(p => ids.Contains(p.ReceivableId))
            .GroupBy(p => p.ReceivableId)
            .Select(g => new { ReceivableId = g.Key, Total = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.ReceivableId, x => x.Total);
    }

    internal static async Task<Dictionary<Guid, List<PaymentSummary>>> ComputePaymentsAsync(IEnumerable<Guid> receivableIds, AbderaDbContext db)
    {
        var ids = receivableIds.ToList();
        var payments = await db.Payments
            .Where(p => ids.Contains(p.ReceivableId))
            .OrderByDescending(p => p.PaymentDate)
            .ThenByDescending(p => p.CreatedAt)
            .ToListAsync();
        return payments
            .GroupBy(p => p.ReceivableId)
            .ToDictionary(g => g.Key, g => g.Select(p => new PaymentSummary(p.Id, p.Amount, p.PaymentDate, p.Method, p.Reference, p.Note)).ToList());
    }

    internal static ReceivableResponse ToResponse(Receivable r, decimal totalPaid, List<PaymentSummary> payments) =>
        new(r.Id, r.EnrollmentId, r.Period, r.Amount, r.Currency, r.DueDate, r.Status, totalPaid, payments);
}
