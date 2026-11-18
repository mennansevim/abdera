using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// docs/10-decisions.md A2: telafi kredisi burada doğar. Öğretmen yalnızca kendi dersini,
// Admin okulun tüm derslerini iptal edebilir; işlem doğrudan audit kaydına düşer.
public static class CancelLesson
{
    public enum CancelledBy { Guardian, School }

    // GrantMakeupCredit: telafi hakkı kararını AÇIKÇA belirler. null (varsayılan) ise
    // docs/10-decisions.md A2'deki otomatik türetme çalışır - eski istemciler ve testler
    // bu yüzden değişmeden çalışır. false, dersi telafi hakkı doğurmadan iptal eder
    // (tatil, yanlış açılmış ders, velinin telafi istemediği geç iptal); true, politikanın
    // kendiliğinden vermeyeceği bir durumda hakkı yine de tanır. Her iki açık seçim de
    // audit kaydına "override" olarak düşer.
    public record Request(CancelledBy CancelledBy, string? Reason, bool? GrantMakeupCredit = null);
    public record Response(Guid LessonId, bool MakeupCreditEarned);

    public static void MapCancelLesson(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lessons/{lessonId:guid}/cancel", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> HandleAsync(
        Guid lessonId, Request request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock, IConfiguration config, INotificationScheduler scheduler,
        IStaffNotifier staffNotifier)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(l => l.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");

        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is { } scopedTeacherId && scopedTeacherId != lesson.TeacherId)
            throw new ForbiddenException("Bu ders size atanmamış.");

        var earnsCredit = await ApplyAsync(
            lesson, request, AuthContext.GetUserId(principal), db, clock, config, scheduler, staffNotifier, notifyStaff: true);

        await db.SaveChangesAsync();
        await staffNotifier.FlushEmailsAsync();
        return Results.Ok(new Response(lesson.Id, earnsCredit));
    }

    // İptalin tek yeri: tekil iptal ve grup dersinin o günkü toplu iptali (LessonGroups.CancelDayAsync)
    // aynı kuralı (job iptali, telafi kararı, audit) buradan geçirir. SaveChanges çağıranın işi.
    // notifyStaff=false: toplu iptal öğrenci başına değil grup başına tek bildirim düşürür.
    internal static async Task<bool> ApplyAsync(
        Lesson lesson, Request request, Guid? actorUserId, AbderaDbContext db, IClock clock, IConfiguration config,
        INotificationScheduler scheduler, IStaffNotifier staffNotifier, bool notifyStaff)
    {
        var now = clock.UtcNow;
        lesson.Cancel(now);

        // docs/10-decisions.md A4: ders iptal olunca bekleyen hatırlatma da iptal edilir -
        // aksi halde veli gerçekleşmeyecek bir ders için hatırlatma alır.
        await scheduler.CancelPendingAsync("lesson", lesson.Id);

        // docs/10-decisions.md A2: okul kaynaklı iptal her zaman kredi doğurur (velinin
        // hatası değil); veli kaynaklı iptal yalnızca yeterli bildirim süresiyle doğurur.
        var noticeHours = config.GetValue("Policy:MakeupNoticeHours", 24);
        var hoursNotice = (lesson.StartAt - now).TotalHours;

        var policyCredit = request.CancelledBy switch
        {
            CancelledBy.School => true,
            CancelledBy.Guardian => hoursNotice >= noticeHours,
            _ => false,
        };

        // Açık seçim politikanın önüne geçer: kullanıcı iptal ekranında "telafisiz iptal"
        // dediğinde okul kaynaklı iptal de kredi doğurmaz.
        var earnsCredit = request.GrantMakeupCredit ?? policyCredit;

        if (earnsCredit)
        {
            var validDays = config.GetValue("Policy:MakeupCreditValidDays", 60);
            var reason = request.CancelledBy == CancelledBy.School
                ? MakeupCreditEarnedReason.SchoolCancelled
                : MakeupCreditEarnedReason.GuardianCancelled24H;

            db.MakeupCredits.Add(MakeupCredit.Earn(lesson.StudentId, lesson.Id, reason, now, validDays));
        }

        db.AuditLogs.Add(AuditLog.Record(
            actorUserId,
            "lesson.cancelled",
            nameof(Lesson),
            lesson.Id,
            now,
            JsonSerializer.Serialize(new { Status = LessonStatus.Normal.ToString() }),
            JsonSerializer.Serialize(new
            {
                Status = lesson.Status.ToString(),
                CancelledBy = request.CancelledBy.ToString(),
                MakeupCreditEarned = earnsCredit,
                // Politika ne derdi / kullanıcı ne dedi - "bu öğrenciye telafi neden
                // verilmedi" sorusu başka tabloya gitmeden yanıtlanabilmeli.
                PolicyMakeupCredit = policyCredit,
                MakeupCreditOverridden = request.GrantMakeupCredit is { } chosen && chosen != policyCredit,
                Reason = request.Reason,
            })));

        if (notifyStaff)
        {
            await LessonChangeNotice.NotifyCancelledAsync(
                staffNotifier, db, clock, actorUserId, lesson.TeacherId, lesson.StudentId,
                lesson.StartAt, lesson.Id, earnsCredit, request.Reason);
        }

        return earnsCredit;
    }
}
