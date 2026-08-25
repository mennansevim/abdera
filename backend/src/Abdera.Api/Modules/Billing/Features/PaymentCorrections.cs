using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

public static class PaymentCorrections
{
    public record CreateRequest(decimal CorrectedAmount, string Reason);
    public record Response(
        Guid Id,
        Guid PaymentId,
        decimal PreviousAmount,
        decimal CorrectedAmount,
        string Reason,
        DateTimeOffset CreatedAt);

    public static void MapPaymentCorrections(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/payments/{paymentId:guid}/corrections", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> CreateAsync(
        Guid paymentId,
        CreateRequest request,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(item => item.Id == paymentId)
            ?? throw new NotFoundException("Ödeme bulunamadı.");
        var receivable = await db.Receivables.SingleAsync(item => item.Id == payment.ReceivableId);
        if (receivable.Status == ReceivableStatus.Cancelled)
            throw new ConflictException("İptal edilmiş bir aidatın ödemesi düzeltilemez.");
        if (request.CorrectedAmount < 0)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["correctedAmount"] = ["Düzeltilen ödeme tutarı negatif olamaz."],
            });
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["reason"] = ["Düzeltme nedeni zorunludur."],
            });

        var latestCorrection = await db.PaymentCorrections
            .Where(item => item.PaymentId == paymentId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync();
        var currentAmount = latestCorrection?.CorrectedAmount ?? payment.Amount;
        if (currentAmount == request.CorrectedAmount)
            throw new ConflictException("Düzeltilen ödeme tutarı mevcut tutarla aynı.");

        var receivablePaymentIds = await db.Payments
            .Where(item => item.ReceivableId == receivable.Id)
            .Select(item => item.Id)
            .ToListAsync();
        var effectiveAmounts = await Receivables.ComputeEffectivePaymentAmountsAsync(receivablePaymentIds, db);
        var newTotal = effectiveAmounts.Values.Sum() - currentAmount + request.CorrectedAmount;
        if (newTotal > receivable.Amount)
            throw new ConflictException("Düzeltme aidatın kalan bakiyesinden fazla ödeme oluşturamaz.");

        var actorId = AuthContext.GetUserId(principal);
        var oldStatus = receivable.Status;
        var correction = PaymentCorrection.Create(
            paymentId,
            currentAmount,
            request.CorrectedAmount,
            request.Reason,
            actorId,
            clock.UtcNow);
        db.PaymentCorrections.Add(correction);
        receivable.RecordPaymentEffect(newTotal, clock.UtcNow);
        db.AuditLogs.Add(AuditLog.Record(
            actorId,
            "payment.corrected",
            nameof(Payment),
            payment.Id,
            clock.UtcNow,
            JsonSerializer.Serialize(new { EffectiveAmount = currentAmount, ReceivableStatus = oldStatus.ToString() }),
            JsonSerializer.Serialize(new { EffectiveAmount = request.CorrectedAmount, NewTotal = newTotal, ReceivableStatus = receivable.Status.ToString(), CorrectionId = correction.Id })));

        await db.SaveChangesAsync();
        return Results.Created(
            $"/api/payments/{paymentId}/corrections/{correction.Id}",
            new Response(correction.Id, correction.PaymentId, correction.PreviousAmount, correction.CorrectedAmount, correction.Reason, correction.CreatedAt));
    }
}
