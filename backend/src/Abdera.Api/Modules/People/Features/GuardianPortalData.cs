using System.Security.Claims;
using Abdera.Api.Modules.Banking.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// Veli panelinin takvim/RSVP dışındaki salt-okunur görünümü. Bu uçlar, velinin kendi
// öğrenci ilişkisiyle sınırlı bir read model döndürür; admin/teacher endpoint'leri tekrar
// kullanılmaz çünkü onların yetki kapsamı veli oturumu için uygun değildir.
public static class GuardianPortalData
{
    public record GuardianReceivableResponse(
        Guid Id, string Period, decimal Amount, string Currency, DateOnly DueDate,
        ReceivableStatus Status, decimal TotalPaid);

    public record GuardianEnrollmentBillingResponse(
        Guid EnrollmentId, Guid StudentId, string StudentName, string InstrumentName,
        string TeacherName, List<GuardianReceivableResponse> Receivables);

    public record GuardianMakeupCreditResponse(
        Guid Id, Guid StudentId, MakeupCreditEarnedReason EarnedReason,
        DateTimeOffset EarnedAt, DateTimeOffset ExpiresAt);

    public record GuardianVirtualIbanResponse(string Iban, string Provider);

    public record GuardianBillingResponse(
        List<GuardianEnrollmentBillingResponse> Enrollments,
        List<GuardianMakeupCreditResponse> MakeupCredits,
        GuardianVirtualIbanResponse? VirtualIban);

    public record GuardianMessageResponse(
        Guid Id, string Body, MessageDirection Direction, DateTimeOffset CreatedAt, DateTimeOffset? SentAt);

    public static void MapGuardianPortalData(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/guardian/me/billing", BillingAsync)
            .RequireAuthorization(AuthorizationPolicies.GuardianOnly);
        app.MapGet("/api/guardian/me/messages", MessagesAsync)
            .RequireAuthorization(AuthorizationPolicies.GuardianOnly);
    }

    private static async Task<IResult> BillingAsync(
        ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var guardianId = AuthContext.GetUserId(principal);
        var studentIds = await db.StudentGuardians
            .Where(sg => sg.GuardianId == guardianId)
            .Select(sg => sg.StudentId)
            .ToListAsync();

        if (studentIds.Count == 0)
        {
            return Results.Ok(new GuardianBillingResponse([], [], null));
        }

        var enrollmentRows = await (
            from enrollment in db.Enrollments
            join student in db.Students on enrollment.StudentId equals student.Id
            join teacher in db.Teachers on enrollment.TeacherId equals teacher.Id
            join instrument in db.Instruments on enrollment.InstrumentId equals instrument.Id
            where studentIds.Contains(enrollment.StudentId)
            orderby student.LastName, student.FirstName, enrollment.StartedAt
            select new
            {
                enrollment.Id,
                enrollment.StudentId,
                StudentName = student.FirstName + " " + student.LastName,
                InstrumentName = instrument.Name,
                TeacherName = teacher.FirstName + " " + teacher.LastName,
            }).ToListAsync();

        var enrollmentIds = enrollmentRows.Select(row => row.Id).ToList();
        var receivables = await db.Receivables
            .Where(receivable => enrollmentIds.Contains(receivable.EnrollmentId))
            .OrderByDescending(receivable => receivable.DueDate)
            .ToListAsync();

        var receivableIds = receivables.Select(receivable => receivable.Id).ToList();
        var paidTotals = await db.Payments
            .Where(payment => receivableIds.Contains(payment.ReceivableId))
            .GroupBy(payment => payment.ReceivableId)
            .Select(group => new { ReceivableId = group.Key, Total = group.Sum(payment => payment.Amount) })
            .ToDictionaryAsync(row => row.ReceivableId, row => row.Total);

        var billing = enrollmentRows.Select(enrollment => new GuardianEnrollmentBillingResponse(
            enrollment.Id,
            enrollment.StudentId,
            enrollment.StudentName,
            enrollment.InstrumentName,
            enrollment.TeacherName,
            receivables
                .Where(receivable => receivable.EnrollmentId == enrollment.Id)
                .Select(receivable => new GuardianReceivableResponse(
                    receivable.Id,
                    receivable.Period,
                    receivable.Amount,
                    receivable.Currency,
                    receivable.DueDate,
                    receivable.Status,
                    paidTotals.GetValueOrDefault(receivable.Id)))
                .ToList()))
            .ToList();

        var now = clock.UtcNow;
        var makeupCredits = await db.MakeupCredits
            .Where(credit => studentIds.Contains(credit.StudentId) &&
                            credit.Status == MakeupCreditStatus.Available &&
                            credit.ExpiresAt >= now)
            .OrderBy(credit => credit.ExpiresAt)
            .Select(credit => new GuardianMakeupCreditResponse(
                credit.Id, credit.StudentId, credit.EarnedReason, credit.EarnedAt, credit.ExpiresAt))
            .ToListAsync();

        var virtualIban = await db.VirtualIbans
            .Where(iban => iban.GuardianId == guardianId && iban.Status == VirtualIbanStatus.Active)
            .Select(iban => new GuardianVirtualIbanResponse(iban.Iban, iban.Provider))
            .SingleOrDefaultAsync();

        return Results.Ok(new GuardianBillingResponse(billing, makeupCredits, virtualIban));
    }

    private static async Task<IResult> MessagesAsync(ClaimsPrincipal principal, AbderaDbContext db)
    {
        var guardianId = AuthContext.GetUserId(principal);
        var messages = await db.WhatsAppMessages
            .Where(message => message.GuardianId == guardianId && message.Direction == MessageDirection.Outbound)
            .OrderByDescending(message => message.CreatedAt)
            .Take(50)
            .Select(message => new GuardianMessageResponse(
                message.Id, message.BodySnapshot, message.Direction, message.CreatedAt, message.SentAt))
            .ToListAsync();

        return Results.Ok(messages);
    }
}
