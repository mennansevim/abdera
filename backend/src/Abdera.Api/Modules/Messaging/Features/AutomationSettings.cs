using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Features;

// Faz 3 (docs/15-product-phases.md): Mesaj Merkezi'ndeki "Otomatik gönderim ayarları" panelini
// gerçek bir uç noktaya bağlar. Önceden bu panel tamamen yerel state'ti (bkz. git geçmişi) -
// artık kalıcı, admin-only ve ayar değiştiğinde bekleyen job'ları yeniden hesaplıyor.
public static class AutomationSettings
{
    public record AutomationSettingsResponse(
        int LessonReminderMinutesBefore, bool IsEnabled, bool AllowAttendingLateResponse, DateTimeOffset UpdatedAt);

    public record UpdateRequest(int LessonReminderMinutesBefore, bool IsEnabled, bool AllowAttendingLateResponse);

    public static void MapAutomationSettings(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notification-automation-settings").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", GetAsync);
        group.MapPut("", UpdateAsync);
    }

    private static async Task<IResult> GetAsync(AbderaDbContext db)
    {
        var settings = await NotificationAutomationSettings.GetCurrentAsync(db);
        return Results.Ok(ToResponse(settings));
    }

    private static async Task<IResult> UpdateAsync(UpdateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);

        var settings = await db.NotificationAutomationSettings.SingleOrDefaultAsync(s => s.Id == NotificationAutomationSettings.SingletonId);
        var isNew = settings is null;
        settings ??= NotificationAutomationSettings.CreateDefault(now);
        if (isNew) db.NotificationAutomationSettings.Add(settings);

        var previousMinutes = settings.LessonReminderMinutesBefore;
        var wasEnabled = settings.IsEnabled;

        settings.Update(request.LessonReminderMinutesBefore, request.IsEnabled, request.AllowAttendingLateResponse, actorId, now);

        if (!settings.IsEnabled && wasEnabled)
        {
            // Otomasyon kapatıldı - bekleyen tüm ders hatırlatmaları iptal edilir. Yeniden
            // açıldığında geçmişe dönük bir "toparlama" yapılmaz (docs/15-product-phases.md:
            // "gönderilmiş mesajlar değiştirilmeyecek" - burada henüz gönderilmemiş ama artık
            // anlamsız hâle gelmiş job'lar için aynı temkinli yaklaşım uygulanıyor).
            var pendingReminders = await db.NotificationJobs
                .Where(j => j.Type == NotificationJobType.LessonReminder && j.Status == NotificationJobStatus.Pending)
                .ToListAsync();
            foreach (var job in pendingReminders)
            {
                job.Cancel(now);
            }
        }
        else if (settings.IsEnabled && previousMinutes != settings.LessonReminderMinutesBefore)
        {
            // Hatırlatma süresi değişti - henüz gönderilmemiş job'ların zamanı, ilgili dersin
            // gerçek başlangıç saatine göre yeniden hesaplanır (docs/15-product-phases.md).
            var pendingReminders = await db.NotificationJobs
                .Where(j => j.Type == NotificationJobType.LessonReminder && j.Status == NotificationJobStatus.Pending && j.ReferenceType == "lesson")
                .ToListAsync();
            var lessonIds = pendingReminders.Select(j => j.ReferenceId).ToList();
            var lessonStartTimes = await db.Lessons
                .Where(l => lessonIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.StartAt);

            foreach (var job in pendingReminders)
            {
                if (lessonStartTimes.TryGetValue(job.ReferenceId, out var startAt))
                {
                    job.Reschedule(startAt.AddMinutes(-settings.LessonReminderMinutesBefore), now);
                }
            }
        }

        db.AuditLogs.Add(AuditLog.Record(actorId, "notification_automation_settings.updated", nameof(NotificationAutomationSettings), settings.Id, now,
            beforeJson: JsonSerializer.Serialize(new { minutesBefore = previousMinutes, isEnabled = wasEnabled }),
            afterJson: JsonSerializer.Serialize(new { minutesBefore = settings.LessonReminderMinutesBefore, isEnabled = settings.IsEnabled, allowAttendingLate = settings.AllowAttendingLateResponse })));

        await db.SaveChangesAsync();

        return Results.Ok(ToResponse(settings));
    }

    private static AutomationSettingsResponse ToResponse(NotificationAutomationSettings settings) => new(
        settings.LessonReminderMinutesBefore, settings.IsEnabled, settings.AllowAttendingLateResponse, settings.UpdatedAt);
}
