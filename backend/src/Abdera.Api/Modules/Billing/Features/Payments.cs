using System.Security.Claims;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

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
        Guid receivableId, CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var receivable = await db.Receivables.SingleOrDefaultAsync(r => r.Id == receivableId)
            ?? throw new NotFoundException("Aidat bulunamadı.");

        if (receivable.Status is ReceivableStatus.Cancelled or ReceivableStatus.Paid)
            throw new ConflictException($"'{receivable.Status}' durumundaki bir aidata ödeme kaydedilemez.");

        var actorId = AuthContext.GetUserId(principal);
        var payment = Payment.Create(receivableId, request.Amount, request.PaymentDate, request.Method, request.Reference, request.Note, actorId, clock.UtcNow);
        db.Payments.Add(payment);

        var totalPaid = await db.Payments.Where(p => p.ReceivableId == receivableId).SumAsync(p => p.Amount) + request.Amount;
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

        await db.SaveChangesAsync();

        return Results.Created($"/api/receivables/{receivableId}/payments/{payment.Id}",
            new PaymentResponse(payment.Id, payment.ReceivableId, payment.Amount, payment.PaymentDate, payment.Method, payment.Reference, payment.Note));
    }
}
