using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Banking.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Banking.Features;

// docs/12-bank-integration.md uçtan uca akış diyagramı. Gerçek sağlayıcı henüz seçilmedi
// (docs/10-decisions.md E1) - imza doğrulaması bu yüzden sağlayıcıya özgü bir şema yerine
// paylaşılan-sır (shared secret) başlığıyla yapılıyor; gerçek sağlayıcı seçilince bu tek
// metot (VerifySignature) o sağlayıcının şemasına göre değiştirilir, geri kalan işleme
// mantığı (idempotency, eşleştirme, Payment oluşturma) değişmez.
public static class Webhooks
{
    public record IncomingTransactionPayload(
        string ProviderTransactionId, string VirtualIban, decimal Amount, string Currency,
        string? SenderName, string? Description, DateTimeOffset ReceivedAt);

    public static void MapWebhooks(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/webhooks/bank", ReceiveAsync).AllowAnonymous();
    }

    private static async Task<IResult> ReceiveAsync(
        HttpRequest request, AbderaDbContext db, IClock clock, IConfiguration config)
    {
        request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(request.Body, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync();
        }
        request.Body.Position = 0;

        var sharedSecret = config["Banking:WebhookSharedSecret"] ?? "";
        var providedSecret = request.Headers["X-Bank-Webhook-Secret"].ToString();
        if (!VerifySharedSecret(providedSecret, sharedSecret))
        {
            return Results.Unauthorized();
        }

        BankTransactionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<BankTransactionPayload>(rawBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        if (payload is null) return Results.BadRequest();

        await ProcessIncomingTransactionAsync(
            "generic", payload.ProviderTransactionId, payload.VirtualIban, payload.Amount, payload.Currency ?? "TRY",
            payload.SenderName, payload.Description, payload.ReceivedAt ?? clock.UtcNow, db, clock);

        return Results.Ok();
    }

    private record BankTransactionPayload(
        string ProviderTransactionId, string VirtualIban, decimal Amount, string? Currency,
        string? SenderName, string? Description, DateTimeOffset? ReceivedAt);

    private static bool VerifySharedSecret(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided)) return false;

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return providedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    // docs/12-bank-integration.md "Uçtan uca akış" ve "Veli ↔ Receivable eşleştirme
    // algoritması" - hem gerçek webhook hem dev simülatörü (DevBankSimulator.cs) burayı
    // çağırır ki iki yol asla birbirinden sapmasın (abdera-notification skill'inin
    // "iki implementasyon senkron kalsın" ilkesinin bankacılık karşılığı).
    internal static async Task ProcessIncomingTransactionAsync(
        string provider, string providerTransactionId, string virtualIban, decimal amount, string currency,
        string? senderName, string? description, DateTimeOffset receivedAt, AbderaDbContext db, IClock clock)
    {
        var alreadyProcessed = await db.BankIncomingTransactions
            .AnyAsync(t => t.Provider == provider && t.ProviderTransactionId == providerTransactionId);
        if (alreadyProcessed) return;

        var iban = await db.VirtualIbans.SingleOrDefaultAsync(v => v.Iban == virtualIban && v.Status == VirtualIbanStatus.Active);
        if (iban is null) return; // Bilinmeyen/pasif IBAN - sağlayıcı tarafı yanlış yapılandırılmış, MVP'de sessizce atlanır.

        var now = clock.UtcNow;
        var transaction = BankIncomingTransaction.Receive(
            iban.Id, provider, providerTransactionId, amount, currency, senderName, description, receivedAt, now);
        db.BankIncomingTransactions.Add(transaction);

        var enrollmentIds = await db.StudentGuardians
            .Where(sg => sg.GuardianId == iban.GuardianId)
            .Join(db.Enrollments, sg => sg.StudentId, e => e.StudentId, (sg, e) => e.Id)
            .ToListAsync();

        var openReceivables = await db.Receivables
            .Where(r => enrollmentIds.Contains(r.EnrollmentId) &&
                        (r.Status == ReceivableStatus.Unpaid || r.Status == ReceivableStatus.Partial || r.Status == ReceivableStatus.Overdue))
            .ToListAsync();

        var totals = await Receivables.ComputeTotalsPaidAsync(openReceivables.Select(r => r.Id), db);
        var candidates = openReceivables
            .Select(r => new PaymentMatcher.Candidate(r.Id, r.Period, r.Amount - totals.GetValueOrDefault(r.Id)))
            .ToList();

        var matchedReceivableId = PaymentMatcher.Match(candidates, amount, description);

        if (matchedReceivableId is { } receivableId)
        {
            var receivable = openReceivables.Single(r => r.Id == receivableId);
            var newTotalPaid = totals.GetValueOrDefault(receivableId) + amount;

            db.Payments.Add(Payment.Create(
                receivableId, amount, DateOnly.FromDateTime(clock.ToSchoolLocal(receivedAt).Date), PaymentMethod.Transfer,
                reference: $"banka:{providerTransactionId}", note: senderName, createdBy: null, now));
            receivable.RecordPaymentEffect(newTotalPaid, now);
            transaction.RecordMatch(receivableId, now);

            db.AuditLogs.Add(AuditLog.Record(null, "receivable.auto_payment_matched", nameof(Receivable), receivableId, now,
                afterJson: JsonSerializer.Serialize(new { amount, provider, providerTransactionId, newStatus = receivable.Status.ToString() })));
        }
        else
        {
            transaction.MarkNeedsReview(now);
        }

        await db.SaveChangesAsync();
    }
}
