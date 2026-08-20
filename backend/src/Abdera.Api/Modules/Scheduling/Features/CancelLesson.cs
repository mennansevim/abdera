using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// docs/10-decisions.md A2: telafi kredisi burada doğar. Öğretmen/veli değil, Admin işler -
// ders değişikliği (reschedule) LessonChangeRequest akışından ayrı, doğrudan bir iptaldir.
public static class CancelLesson
{
    public enum CancelledBy { Guardian, School }

    public record Request(CancelledBy CancelledBy, string? Reason);
    public record Response(Guid LessonId, bool MakeupCreditEarned);

    public static void MapCancelLesson(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lessons/{lessonId:guid}/cancel", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> HandleAsync(
        Guid lessonId, Request request, AbderaDbContext db, IClock clock, IConfiguration config, INotificationScheduler scheduler)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(l => l.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");

        var now = clock.UtcNow;
        lesson.Cancel(now);

        // docs/10-decisions.md A4: ders iptal olunca bekleyen hatırlatma da iptal edilir -
        // aksi halde veli gerçekleşmeyecek bir ders için hatırlatma alır.
        await scheduler.CancelPendingAsync("lesson", lesson.Id);

        // docs/10-decisions.md A2: okul kaynaklı iptal her zaman kredi doğurur (velinin
        // hatası değil); veli kaynaklı iptal yalnızca yeterli bildirim süresiyle doğurur.
        var noticeHours = config.GetValue("Policy:MakeupNoticeHours", 24);
        var hoursNotice = (lesson.StartAt - now).TotalHours;

        var earnsCredit = request.CancelledBy switch
        {
            CancelledBy.School => true,
            CancelledBy.Guardian => hoursNotice >= noticeHours,
            _ => false,
        };

        if (earnsCredit)
        {
            var validDays = config.GetValue("Policy:MakeupCreditValidDays", 60);
            var reason = request.CancelledBy == CancelledBy.School
                ? MakeupCreditEarnedReason.SchoolCancelled
                : MakeupCreditEarnedReason.GuardianCancelled24H;

            db.MakeupCredits.Add(MakeupCredit.Earn(lesson.StudentId, lesson.Id, reason, now, validDays));
        }

        await db.SaveChangesAsync();
        return Results.Ok(new Response(lesson.Id, earnsCredit));
    }
}
