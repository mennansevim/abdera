using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Pricing.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// Toplu tahsilat tek transaction içinde aylara dağıtılır. Böylece 10/12 aylık ödeme
// yarım kalırsa bazı aylar işaretlenip kalan aylar kaybolmaz; ödeme ve aidat kayıtları
// aynı SaveChanges çağrısında birlikte oluşur.
public static class BulkPayments
{
    public record CreateRequest(
        Guid EnrollmentId,
        string StartPeriod,
        int Months,
        decimal Amount,
        DateOnly PaymentDate,
        PaymentMethod Method,
        string? Reference,
        string? Note);

    public static void MapBulkPayments(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/enrollments/{enrollmentId:guid}/bulk-payments", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> CreateAsync(
        Guid enrollmentId,
        CreateRequest request,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock)
    {
        if (request.EnrollmentId != enrollmentId)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["enrollmentId"] = ["Kayıt bilgisi istek gövdesiyle eşleşmiyor."] });
        if (request.Months is < 1 or > 24)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["months"] = ["Toplu ödeme 1 ile 24 ay arasında olmalı."] });
        if (request.Amount <= 0)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["amount"] = ["Toplam ödeme tutarı pozitif olmalı."] });
        if (!DateOnly.TryParseExact(request.StartPeriod, "yyyy-MM", out var startPeriod))
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["startPeriod"] = ["Başlangıç dönemi 'yyyy-MM' biçiminde olmalı."] });

        var feePlan = await db.FeePlans
            .SingleOrDefaultAsync(f => f.EnrollmentId == enrollmentId && f.ActiveUntil == null)
            ?? throw new NotFoundException("Bu kayıt için aktif bir ücret planı bulunamadı.");
        if (feePlan.BillingType != BillingType.Monthly)
            throw new ConflictException("Toplu ay ödemesi yalnızca aylık ücret planlarında kullanılabilir.");

        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);
        var requestedPeriods = Enumerable.Range(0, request.Months)
            .Select(offset => startPeriod.AddMonths(offset).ToString("yyyy-MM"))
            .ToList();
        var existing = await db.Receivables
            .Where(r => r.EnrollmentId == enrollmentId && requestedPeriods.Contains(r.Period))
            .ToDictionaryAsync(r => r.Period);
        var targets = new List<Receivable>();

        foreach (var period in requestedPeriods)
        {
            if (existing.TryGetValue(period, out var receivable))
            {
                targets.Add(receivable);
                continue;
            }

            var dueDate = new DateOnly(int.Parse(period[..4]), int.Parse(period[5..]), feePlan.DueDay ?? 1);
            var created = Receivable.Create(enrollmentId, feePlan.Id, feePlan.PriceListItemId, period, feePlan.Amount, feePlan.Currency, dueDate, now);
            db.Receivables.Add(created);
            targets.Add(created);
        }

        var targetIds = targets.Where(r => r.Id != Guid.Empty).Select(r => r.Id).ToList();
        var paidByReceivable = await Receivables.ComputeTotalsPaidAsync(targetIds, db);

        var remaining = request.Amount;
        var actorId = AuthContext.GetUserId(principal);
        foreach (var receivable in targets.OrderBy(r => r.Period))
        {
            if (receivable.Status is ReceivableStatus.Cancelled or ReceivableStatus.Paid) continue;
            var outstanding = receivable.Amount - paidByReceivable.GetValueOrDefault(receivable.Id);
            if (outstanding <= 0) continue;

            var applied = Math.Min(outstanding, remaining);
            if (applied <= 0) break;

            var payment = Payment.Create(receivable.Id, applied, request.PaymentDate, request.Method, request.Reference, request.Note, actorId, now);
            db.Payments.Add(payment);
            receivable.RecordPaymentEffect(paidByReceivable.GetValueOrDefault(receivable.Id) + applied, now);
            db.AuditLogs.Add(AuditLog.Record(actorId, "receivable.bulk_payment_recorded", nameof(Receivable), receivable.Id, now,
                afterJson: JsonSerializer.Serialize(new { amount = applied, period = receivable.Period, months = request.Months, newStatus = receivable.Status.ToString() })));
            remaining -= applied;
        }

        if (remaining > 0.01m)
            throw new ConflictException($"Toplam ödeme seçilen ayların kalan borcunu aşıyor. Artan tutar: {remaining:0.##} {feePlan.Currency}.");

        await db.SaveChangesAsync();
        var totals = await Receivables.ComputeTotalsPaidAsync(targets.Select(r => r.Id), db);
        var payments = await Receivables.ComputePaymentsAsync(targets.Select(r => r.Id), db);
        return Results.Ok(targets.Select(r => Receivables.ToResponse(r, totals.GetValueOrDefault(r.Id), payments.GetValueOrDefault(r.Id) ?? [])).ToList());
    }
}
