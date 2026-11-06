using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// GET /api/billing/payment-activity - Gelir ve gider > Hareketler: tahsilata dokunan her kayıt,
// girildiği ana göre (en yeni önce). Tahsilat defteri parayı ödeme tarihine göre gösterir; burası
// "ne zaman, kim, ne girdi" sorusunu yanıtlar: alınan ödeme, tutar düzeltmesi, tarih düzeltmesi.
// Satırlar özgün tablolardan okunur (payments, payment_corrections, payment_date_corrections) -
// hiçbiri silinmediği için geçmiş eksiksizdir.
public static class PaymentActivity
{
    public enum ActivityKind { Received, AmountCorrected, DateCorrected }

    public record ActivityResponse(
        Guid Id, ActivityKind Kind, DateTimeOffset RecordedAt, string? ActorName,
        Guid PaymentId, Guid ReceivableId, string StudentName, string InstrumentName, CourseKind CourseKind, string Period,
        PaymentMethod Method, decimal Amount, decimal? PreviousAmount, DateOnly PaymentDate, DateOnly? PreviousDate, string? Note);

    public static void MapPaymentActivity(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/billing/payment-activity", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> HandleAsync(AbderaDbContext db)
    {
        var payments = await db.Payments.AsNoTracking().ToListAsync();
        var paymentsById = payments.ToDictionary(payment => payment.Id);
        var amountCorrections = await db.PaymentCorrections.AsNoTracking().ToListAsync();
        var dateCorrections = await db.PaymentDateCorrections.AsNoTracking().ToListAsync();

        var receivableIds = payments.Select(payment => payment.ReceivableId).Distinct().ToList();
        var receivables = await db.Receivables.AsNoTracking().Where(r => receivableIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id);
        var enrollmentIds = receivables.Values.Select(r => r.EnrollmentId).Distinct().ToList();
        var enrollments = await db.Enrollments.AsNoTracking().Where(e => enrollmentIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id);
        var studentIds = enrollments.Values.Select(e => e.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var instrumentIds = enrollments.Values.Select(e => e.InstrumentId).Distinct().ToList();
        var instruments = await db.Instruments.AsNoTracking().Where(i => instrumentIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

        var actorIds = payments.Where(p => p.CreatedBy is not null).Select(p => p.CreatedBy!.Value)
            .Concat(amountCorrections.Select(c => c.CreatedBy))
            .Concat(dateCorrections.Select(c => c.CreatedBy))
            .Distinct().ToList();
        var actors = await db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName ?? u.Email);
        // Otomatik eşleşen banka havalesinde CreatedBy null'dır: kaydı giren bir yönetici yok.
        string? Actor(Guid? id) => id is { } value ? actors.GetValueOrDefault(value) : null;

        ActivityResponse Row(Guid id, ActivityKind kind, DateTimeOffset recordedAt, Guid? actorId, Payment payment,
            decimal amount, decimal? previousAmount, DateOnly date, DateOnly? previousDate, string? note)
        {
            var receivable = receivables[payment.ReceivableId];
            var enrollment = enrollments[receivable.EnrollmentId];
            var student = students[enrollment.StudentId];
            return new ActivityResponse(
                id, kind, recordedAt, Actor(actorId), payment.Id, receivable.Id, $"{student.FirstName} {student.LastName}",
                instruments[enrollment.InstrumentId].Name, enrollment.CourseKind, receivable.Period, payment.Method,
                amount, previousAmount, date, previousDate, note);
        }

        var rows = payments
            .Select(p => Row(p.Id, ActivityKind.Received, p.CreatedAt, p.CreatedBy, p, p.Amount, null, p.PaymentDate, null, p.Note))
            .Concat(amountCorrections.Select(c =>
            {
                var payment = paymentsById[c.PaymentId];
                return Row(c.Id, ActivityKind.AmountCorrected, c.CreatedAt, c.CreatedBy, payment, c.CorrectedAmount, c.PreviousAmount, payment.PaymentDate, null, c.Reason);
            }))
            .Concat(dateCorrections.Select(c =>
            {
                var payment = paymentsById[c.PaymentId];
                return Row(c.Id, ActivityKind.DateCorrected, c.CreatedAt, c.CreatedBy, payment, payment.Amount, null, c.CorrectedDate, c.PreviousDate, c.Reason);
            }))
            .OrderByDescending(row => row.RecordedAt)
            .ThenBy(row => row.Kind)
            .ToList();

        return Results.Ok(rows);
    }
}
