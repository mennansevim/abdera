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

    public record DateCorrectionRequest(List<Guid> PaymentIds, DateOnly PaymentDate, string Reason);
    public record DateCorrectionResponse(Guid PaymentId, DateOnly PreviousDate, DateOnly CorrectedDate);

    public static void MapPaymentCorrections(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/payments/{paymentId:guid}/corrections", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost("/api/payments/date-corrections", CorrectDatesAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    // Tahsilat penceresi ödeme tarihini "bugün" ile doldurur; geçen ay alınan para sonradan
    // işlenince gelir yanlış aya yazılır. Birden fazla ödeme tek istekte, tek transaction'da
    // aynı tarihe çekilir. Peşin ödeme planının parçaları tek bir tahsilattır: biri seçilince
    // planın tamamı birlikte taşınır, yoksa tek bir ödeme iki aya bölünürdü.
    private static async Task<IResult> CorrectDatesAsync(
        DateCorrectionRequest request,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.PaymentIds is not { Count: > 0 }) errors["paymentIds"] = ["En az bir ödeme seçilmeli."];
        if (string.IsNullOrWhiteSpace(request.Reason)) errors["reason"] = ["Düzeltme nedeni zorunludur."];
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        if (request.PaymentDate > today) errors["paymentDate"] = ["Ödeme tarihi ileri bir gün olamaz."];
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        var requestedIds = request.PaymentIds!.Distinct().ToList();
        var selected = await db.Payments.Where(item => requestedIds.Contains(item.Id)).ToListAsync();
        if (selected.Count != requestedIds.Count) throw new NotFoundException("Ödeme bulunamadı.");
        var planIds = selected.Where(item => item.PrepayPlanId.HasValue).Select(item => item.PrepayPlanId!.Value).Distinct().ToList();
        var payments = planIds.Count == 0
            ? selected
            : await db.Payments.Where(item => requestedIds.Contains(item.Id) || (item.PrepayPlanId.HasValue && planIds.Contains(item.PrepayPlanId.Value))).ToListAsync();

        var receivableIds = payments.Select(item => item.ReceivableId).Distinct().ToList();
        if (await db.Receivables.AnyAsync(item => receivableIds.Contains(item.Id) && item.Status == ReceivableStatus.Cancelled))
            throw new ConflictException("İptal edilmiş bir aidatın ödemesinin tarihi düzeltilemez.");

        var currentDates = await Receivables.ComputeEffectivePaymentDatesAsync(payments.Select(item => item.Id), db);
        var changing = payments.Where(item => currentDates[item.Id] != request.PaymentDate).ToList();
        if (changing.Count == 0) throw new ConflictException("Seçilen ödemelerin tarihi zaten bu gün.");

        var actorId = AuthContext.GetUserId(principal);
        var now = clock.UtcNow;
        var results = new List<DateCorrectionResponse>();
        foreach (var payment in changing)
        {
            var previous = currentDates[payment.Id];
            var correction = PaymentDateCorrection.Create(payment.Id, previous, request.PaymentDate, request.Reason, today, actorId, now);
            db.PaymentDateCorrections.Add(correction);
            db.AuditLogs.Add(AuditLog.Record(
                actorId,
                "payment.date_corrected",
                nameof(Payment),
                payment.Id,
                now,
                JsonSerializer.Serialize(new { PaymentDate = previous }),
                JsonSerializer.Serialize(new { PaymentDate = request.PaymentDate, Reason = correction.Reason, CorrectionId = correction.Id })));
            results.Add(new DateCorrectionResponse(payment.Id, previous, request.PaymentDate));
        }

        await db.SaveChangesAsync();
        return Results.Ok(results);
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
        // Tahsilat geri alınınca (tutar 0'a / aşağı düzeltilince) RecordPaymentEffect aidatı
        // Unpaid/Partial'a indirir; vadesi geçmişse gece taramasını (OverdueReceivableSweeper)
        // beklemeden hemen Overdue görünmeli - yoksa geri alınan bir ay bir gün boyunca
        // "ödenmedi ama gecikmedi" diye yanlış görünürdü.
        receivable.MarkOverdueIfPastDue(DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date), clock.UtcNow);
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
