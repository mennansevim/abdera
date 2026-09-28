using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.Progress.Infrastructure;
using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Progress.Features;

// Vercel Cron'un günlük çağırdığı uç (vercel.json "crons") - serverless yayında
// LessonNoteReminderWorker çalışmadığı için yorum hatırlatması buradan tazelenir. Kalıcı
// container kurulumunda gereksiz ama zararsız (idempotent, günde bir kez okunmamışa döndürür).
// Kimlik doğrulama: CronAuth (CRON_SECRET yoksa 404, yanlış sırla 401).
public static class LessonNoteReminderCron
{
    public static void MapLessonNoteReminderCron(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/internal/cron/lesson-note-reminders", RunAsync).AllowAnonymous();
    }

    private static async Task<IResult> RunAsync(
        HttpContext context, IConfiguration configuration, AbderaDbContext db, IClock clock,
        IStaffNotifier notifier, ILoggerFactory loggerFactory)
    {
        if (CronAuth.Reject(context, configuration) is { } rejection) return rejection;

        var result = await LessonNoteReminderJob.RunAsync(db, clock, notifier, context.RequestAborted);
        loggerFactory.CreateLogger("Abdera.Progress.LessonNoteReminderCron").LogInformation(
            "Ders yorumu hatırlatması: {Teachers} öğretmende {Pending} ders yorum bekliyor, {Reminded} hatırlatma düştü, {Cleared} kapandı.",
            result.TeachersWithPendingNotes, result.PendingLessonCount, result.RemindedCount, result.ClearedCount);
        return Results.Ok(result);
    }
}
