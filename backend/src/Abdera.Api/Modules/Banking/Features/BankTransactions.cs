using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Banking.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Banking.Features;

// docs/12-bank-integration.md "Belirsiz işlemin elle çözülmesi". NeedsReview durumundaki
// işlemleri admin panelinde listeler, admin'in elle bir Receivable'a bağlamasını sağlar.
public static class BankTransactions
{
    public record TransactionResponse(
        Guid Id, Guid VirtualIbanId, Guid GuardianId, decimal Amount, string Currency,
        string? SenderName, string? Description, DateTimeOffset ReceivedAt,
        BankIncomingTransactionStatus Status, Guid? MatchedReceivableId);

    public record ResolveRequest(Guid? ReceivableId);

    public static void MapBankTransactions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bank-transactions").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", ListAsync);
        group.MapPost("/{transactionId:guid}/resolve", ResolveAsync);
    }

    private static async Task<IResult> ListAsync(BankIncomingTransactionStatus? status, AbderaDbContext db)
    {
        var query = db.BankIncomingTransactions.AsQueryable();
        if (status is { } s) query = query.Where(t => t.Status == s);

        var transactions = await query.OrderByDescending(t => t.ReceivedAt).Take(200).ToListAsync();
        var virtualIbanIds = transactions.Select(t => t.VirtualIbanId).Distinct().ToList();
        var virtualIbans = await db.VirtualIbans.Where(v => virtualIbanIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.GuardianId);

        return Results.Ok(transactions.Select(t => new TransactionResponse(
            t.Id, t.VirtualIbanId, virtualIbans.GetValueOrDefault(t.VirtualIbanId), t.Amount, t.Currency,
            t.SenderName, t.Description, t.ReceivedAt, t.Status, t.MatchedReceivableId)));
    }

    private static async Task<IResult> ResolveAsync(
        Guid transactionId, ResolveRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var transaction = await db.BankIncomingTransactions.SingleOrDefaultAsync(t => t.Id == transactionId)
            ?? throw new NotFoundException("Banka işlemi bulunamadı.");

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var guardianId = await db.VirtualIbans.Where(v => v.Id == transaction.VirtualIbanId).Select(v => v.GuardianId).SingleAsync();

        if (request.ReceivableId is null)
        {
            transaction.Ignore(now);
            await db.SaveChangesAsync();
            return Results.Ok(new TransactionResponse(
                transaction.Id, transaction.VirtualIbanId, guardianId, transaction.Amount, transaction.Currency,
                transaction.SenderName, transaction.Description, transaction.ReceivedAt, transaction.Status, transaction.MatchedReceivableId));
        }

        var receivable = await db.Receivables.SingleOrDefaultAsync(r => r.Id == request.ReceivableId)
            ?? throw new NotFoundException("Aidat bulunamadı.");
        if (receivable.Status is ReceivableStatus.Cancelled or ReceivableStatus.Paid)
            throw new ConflictException($"'{receivable.Status}' durumundaki bir aidata ödeme kaydedilemez.");

        var totalPaid = await db.Payments.Where(p => p.ReceivableId == receivable.Id).SumAsync(p => p.Amount) + transaction.Amount;

        db.Payments.Add(Payment.Create(
            receivable.Id, transaction.Amount, DateOnly.FromDateTime(clock.ToSchoolLocal(transaction.ReceivedAt).Date),
            PaymentMethod.Transfer, reference: $"banka:{transaction.ProviderTransactionId}", note: transaction.SenderName,
            createdBy: actorId, now));
        receivable.RecordPaymentEffect(totalPaid, now);
        transaction.RecordMatch(receivable.Id, now);

        db.AuditLogs.Add(AuditLog.Record(actorId, "receivable.bank_transaction_manually_resolved", nameof(Receivable), receivable.Id, now,
            afterJson: JsonSerializer.Serialize(new { amount = transaction.Amount, transactionId = transaction.Id, newStatus = receivable.Status.ToString() })));

        await db.SaveChangesAsync();

        return Results.Ok(new TransactionResponse(
            transaction.Id, transaction.VirtualIbanId, guardianId, transaction.Amount, transaction.Currency,
            transaction.SenderName, transaction.Description, transaction.ReceivedAt, transaction.Status, transaction.MatchedReceivableId));
    }
}
