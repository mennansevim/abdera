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
        BankIncomingTransactionStatus Status, Guid? MatchedReceivableId, string? GuardianName = null);

    public record ResolveRequest(Guid? ReceivableId);

    // Elle çözümde seçilebilecek aidat. Eskiden ekran bir "Aidat ID" (UUID) yazılmasını istiyordu
    // ama kimlik arayüzün hiçbir yerinde görünmüyordu; işlem fiilen eşleştirilemiyordu.
    // FitsRemainingBalance: gelen tutar kalan bakiyeyi aşmıyor; aşan aday ResolveAsync içinde reddedilir.
    public record CandidateResponse(
        Guid ReceivableId, string Period, string StudentName, string InstrumentName,
        decimal Amount, decimal RemainingBalance, string Currency, ReceivableStatus Status, bool FitsRemainingBalance);

    public static void MapBankTransactions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bank-transactions").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", ListAsync);
        group.MapGet("/{transactionId:guid}/candidates", CandidatesAsync);
        group.MapPost("/{transactionId:guid}/resolve", ResolveAsync);
    }

    private static async Task<IResult> ListAsync(BankIncomingTransactionStatus? status, int? page, int? pageSize, AbderaDbContext db)
    {
        var (normalizedPage, normalizedPageSize) = Pagination.Normalize(page, pageSize);

        var query = db.BankIncomingTransactions.AsQueryable();
        if (status is { } s) query = query.Where(t => t.Status == s);

        var totalCount = await query.CountAsync();
        var transactions = await query
            .OrderByDescending(t => t.ReceivedAt)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();
        var virtualIbanIds = transactions.Select(t => t.VirtualIbanId).Distinct().ToList();
        var virtualIbans = await db.VirtualIbans.Where(v => virtualIbanIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.GuardianId);
        var guardianIds = virtualIbans.Values.Distinct().ToList();
        var guardianNames = await db.Guardians.Where(g => guardianIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.FirstName + " " + g.LastName);

        var items = transactions.Select(t =>
        {
            var guardianId = virtualIbans.GetValueOrDefault(t.VirtualIbanId);
            return new TransactionResponse(
                t.Id, t.VirtualIbanId, guardianId, t.Amount, t.Currency,
                t.SenderName, t.Description, t.ReceivedAt, t.Status, t.MatchedReceivableId, guardianNames.GetValueOrDefault(guardianId));
        }).ToList();

        return Results.Ok(new PagedResponse<TransactionResponse>(items, totalCount, normalizedPage, normalizedPageSize));
    }

    private static async Task<IResult> CandidatesAsync(Guid transactionId, AbderaDbContext db)
    {
        var transaction = await db.BankIncomingTransactions.SingleOrDefaultAsync(t => t.Id == transactionId)
            ?? throw new NotFoundException("Banka işlemi bulunamadı.");
        var guardianId = await db.VirtualIbans.Where(v => v.Id == transaction.VirtualIbanId).Select(v => v.GuardianId).SingleAsync();

        var receivables = await Webhooks.LoadOpenReceivablesForGuardianAsync(guardianId, db);
        var totals = await Receivables.ComputeTotalsPaidAsync(receivables.Select(r => r.Id), db);

        var enrollmentIds = receivables.Select(r => r.EnrollmentId).Distinct().ToList();
        var labels = await db.Enrollments
            .Where(e => enrollmentIds.Contains(e.Id))
            .Join(db.Students, e => e.StudentId, st => st.Id, (e, st) => new { e.Id, e.InstrumentId, StudentName = st.FirstName + " " + st.LastName })
            .Join(db.Instruments, x => x.InstrumentId, i => i.Id, (x, i) => new { x.Id, x.StudentName, InstrumentName = i.Name })
            .ToDictionaryAsync(x => x.Id);

        var items = receivables
            .Select(r =>
            {
                var remaining = r.Amount - totals.GetValueOrDefault(r.Id);
                var label = labels.GetValueOrDefault(r.EnrollmentId);
                return new CandidateResponse(
                    r.Id, r.Period, label?.StudentName ?? "", label?.InstrumentName ?? "",
                    r.Amount, remaining, r.Currency, r.Status, transaction.Amount <= remaining);
            })
            .OrderBy(c => c.Period).ThenBy(c => c.StudentName)
            .ToList();
        return Results.Ok(items);
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

        var totalPaid = (await Receivables.ComputeTotalsPaidAsync([receivable.Id], db)).GetValueOrDefault(receivable.Id) + transaction.Amount;
        if (totalPaid > receivable.Amount)
            throw new ConflictException("Banka işlemi aidatın kalan bakiyesini aşıyor.");

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
