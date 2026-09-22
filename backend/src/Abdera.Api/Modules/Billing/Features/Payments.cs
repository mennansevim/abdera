using System.Security.Claims;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Abdera.Api.Modules.Billing.Features;

// docs/07-api.md POST /api/receivables/{id}/payments. Master prompt "Payment" akışı: "...
// Administrator records payment -> Recalculate receivable status." CLAUDE.md: para
// değiştiren her use-case audit yazar.
public static class Payments
{
    public record CreateRequest(decimal Amount, DateOnly PaymentDate, PaymentMethod Method, string? Reference, string? Note);
    public record PaymentResponse(Guid Id, Guid ReceivableId, decimal Amount, DateOnly PaymentDate, PaymentMethod Method, string? Reference, string? Note);

    public static void MapPayments(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/receivables/{receivableId:guid}/payments", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> CreateAsync(
        Guid receivableId, CreateRequest request, HttpRequest httpRequest,
        ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var idempotencyKey = httpRequest.Headers["Idempotency-Key"].ToString().Trim();
        if (idempotencyKey.Length is < 8 or > 100)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = ["Tahsilat isteği 8-100 karakterlik bir Idempotency-Key başlığı taşımalı."],
            });
        }

        var actorId = AuthContext.GetUserId(principal);
        var existing = await db.Payments.SingleOrDefaultAsync(payment => payment.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            EnsureSameRequest(existing, receivableId, request, actorId);
            return Created(existing);
        }

        var receivable = await db.Receivables.SingleOrDefaultAsync(r => r.Id == receivableId)
            ?? throw new NotFoundException("Aidat bulunamadı.");

        if (receivable.Status is ReceivableStatus.Cancelled or ReceivableStatus.Paid)
            throw new ConflictException($"'{receivable.Status}' durumundaki bir aidata ödeme kaydedilemez.");

        var payment = Payment.Create(
            receivableId, request.Amount, request.PaymentDate, request.Method,
            request.Reference, request.Note, actorId, clock.UtcNow,
            idempotencyKey: idempotencyKey);
        db.Payments.Add(payment);

        var existingPaymentIds = await db.Payments
            .Where(item => item.ReceivableId == receivableId)
            .Select(item => item.Id)
            .ToListAsync();
        var effectiveAmounts = await Receivables.ComputeEffectivePaymentAmountsAsync(existingPaymentIds, db);
        var totalPaid = effectiveAmounts.Values.Sum() + request.Amount;
        if (totalPaid > receivable.Amount)
            throw new ConflictException("Ödeme aidatın kalan bakiyesini aşamaz.");
        receivable.RecordPaymentEffect(totalPaid, clock.UtcNow);

        // JsonSerializer kullanılır - CLAUDE.md "Çok tablolu sorgularda OrderBy sırası"
        // notunun yanına eklenen benzer bir ders: decimal'i string interpolation ile JSON'a
        // basmak kültüre bağımlı geçersiz JSON üretebilir (jsonb kolonu reddeder).
        db.AuditLogs.Add(AuditLog.Record(actorId, "receivable.payment_recorded", nameof(Receivable), receivable.Id, clock.UtcNow,
            afterJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                amount = payment.Amount,
                method = payment.Method.ToString(),
                newStatus = receivable.Status.ToString(),
            })));

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "ux_payments_idempotency_key",
            })
        {
            // İki aynı istek tam aynı anda geldiyse unique kısıt yalnızca birini geçirir.
            // Kaybeden isteğin tüm transaction'ı geri alınmıştır; kazanan kaydı okuyup aynı
            // sonucu döndürmek gerçek retry-safe davranıştır.
            db.ChangeTracker.Clear();
            var concurrent = await db.Payments.SingleAsync(item => item.IdempotencyKey == idempotencyKey);
            EnsureSameRequest(concurrent, receivableId, request, actorId);
            return Created(concurrent);
        }

        return Created(payment);
    }

    private static IResult Created(Payment payment) => Results.Created(
        $"/api/receivables/{payment.ReceivableId}/payments/{payment.Id}",
        new PaymentResponse(
            payment.Id, payment.ReceivableId, payment.Amount, payment.PaymentDate,
            payment.Method, payment.Reference, payment.Note));

    private static void EnsureSameRequest(
        Payment payment, Guid receivableId, CreateRequest request, Guid actorId)
    {
        static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        if (payment.ReceivableId != receivableId ||
            payment.Amount != request.Amount ||
            payment.PaymentDate != request.PaymentDate ||
            payment.Method != request.Method ||
            payment.Reference != Normalize(request.Reference) ||
            payment.Note != Normalize(request.Note) ||
            payment.CreatedBy != actorId)
        {
            throw new ConflictException("Bu Idempotency-Key farklı bir tahsilat isteğinde zaten kullanılmış.");
        }
    }
}
