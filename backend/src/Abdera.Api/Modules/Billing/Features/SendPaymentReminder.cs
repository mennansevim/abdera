using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// docs/07-api.md POST /api/receivables/{id}/send-reminder - Phase 4'te Messaging henüz
// yokken ertelenmişti (docs/10-decisions.md), Phase 5 ile birlikte tamamlanıyor.
public static class SendPaymentReminder
{
    public record Response(bool Scheduled, ReminderScheduleOutcome Outcome);

    public static void MapSendPaymentReminder(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/receivables/{receivableId:guid}/send-reminder", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> HandleAsync(
        Guid receivableId, AbderaDbContext db, IClock clock, INotificationScheduler scheduler)
    {
        var receivable = await db.Receivables.SingleOrDefaultAsync(r => r.Id == receivableId)
            ?? throw new NotFoundException("Aidat bulunamadı.");

        if (receivable.Status is ReceivableStatus.Paid or ReceivableStatus.Cancelled)
            throw new ConflictException("Ödenmiş veya iptal edilmiş aidat için hatırlatma gönderilemez.");

        var enrollment = await db.Enrollments.SingleAsync(e => e.Id == receivable.EnrollmentId);
        var primaryGuardianId = await PrimaryGuardianResolver.ResolveAsync(db, enrollment.StudentId);

        // Elle tetiklenen bir hatırlatma - A6 sessiz saat kontrolü yine de uygulanır
        // (NotificationDispatcher PaymentReminder tipini otomatik öteler).
        var outcome = primaryGuardianId is { } guardianId
            ? await scheduler.ScheduleRepeatableAsync(
                NotificationJobType.PaymentReminder, "receivable", receivable.Id, guardianId, clock.UtcNow)
            : ReminderScheduleOutcome.NoGuardian;

        await db.SaveChangesAsync();
        return Results.Ok(new Response(outcome == ReminderScheduleOutcome.Scheduled, outcome));
    }
}
