using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

public static class InstrumentMaintenance
{
    public record UpsertRequest(
        string MaintenanceType, int PeriodDays, bool IsEnabled,
        MaintenanceNotificationPreference NotificationPreference, DateTimeOffset NextReminderAt);
    public record Response(
        Guid Id, Guid InstrumentId, string InstrumentName, string MaintenanceType, int PeriodDays,
        bool IsEnabled, MaintenanceNotificationPreference NotificationPreference,
        DateTimeOffset NextReminderAt, int ConsentingGuardianCount);

    public static void MapInstrumentMaintenance(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/instrument-maintenance-settings", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPut("/api/instruments/{instrumentId:guid}/maintenance-setting", UpsertAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapPost("/api/instrument-maintenance-settings/run-due", RunDueAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db)
    {
        var settings = await db.InstrumentMaintenanceSettings.OrderBy(setting => setting.MaintenanceType).ToListAsync();
        var result = new List<Response>();
        foreach (var setting in settings) result.Add(await ToResponseAsync(setting, db));
        return Results.Ok(result);
    }

    private static async Task<IResult> UpsertAsync(
        Guid instrumentId, UpsertRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        if (!await db.Instruments.AnyAsync(instrument => instrument.Id == instrumentId))
            throw new NotFoundException("Enstrüman bulunamadı.");
        var setting = await db.InstrumentMaintenanceSettings.SingleOrDefaultAsync(item => item.InstrumentId == instrumentId);
        var action = setting is null ? "instrument_maintenance.created" : "instrument_maintenance.updated";
        if (setting is null)
        {
            setting = InstrumentMaintenanceSetting.Create(
                instrumentId, request.MaintenanceType, request.PeriodDays, request.IsEnabled,
                request.NotificationPreference, request.NextReminderAt, clock.UtcNow);
            db.InstrumentMaintenanceSettings.Add(setting);
        }
        else
        {
            setting.Update(request.MaintenanceType, request.PeriodDays, request.IsEnabled,
                request.NotificationPreference, request.NextReminderAt, clock.UtcNow);
        }
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal), action, nameof(InstrumentMaintenanceSetting), setting.Id, clock.UtcNow,
            null, JsonSerializer.Serialize(new { setting.InstrumentId, setting.MaintenanceType, setting.PeriodDays, setting.IsEnabled, setting.NotificationPreference, setting.NextReminderAt })));
        await db.SaveChangesAsync();
        return Results.Ok(await ToResponseAsync(setting, db));
    }

    private static async Task<IResult> RunDueAsync(
        ClaimsPrincipal principal, AbderaDbContext db, IClock clock, INotificationScheduler scheduler)
    {
        var now = clock.UtcNow;
        var due = await db.InstrumentMaintenanceSettings
            .Where(setting => setting.IsEnabled &&
                              setting.NotificationPreference == MaintenanceNotificationPreference.WhatsApp &&
                              setting.NextReminderAt <= now)
            .ToListAsync();
        var scheduled = 0;
        foreach (var setting in due)
        {
            var guardianIds = await (
                from enrollment in db.Enrollments
                join link in db.StudentGuardians on enrollment.StudentId equals link.StudentId
                join guardian in db.Guardians on link.GuardianId equals guardian.Id
                where enrollment.InstrumentId == setting.InstrumentId &&
                      enrollment.Status == EnrollmentStatus.Active && guardian.NotificationConsent
                select guardian.Id).Distinct().ToListAsync();
            foreach (var guardianId in guardianIds)
            {
                var reminder = InstrumentMaintenanceReminder.Create(setting.Id, guardianId, now);
                db.InstrumentMaintenanceReminders.Add(reminder);
                if (await scheduler.ScheduleAsync(NotificationJobType.InstrumentMaintenance, "instrument-maintenance", reminder.Id, guardianId, now))
                    scheduled++;
            }
            setting.AdvanceAfter(now);
        }
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal), "instrument_maintenance.due_run", "InstrumentMaintenance", Guid.Empty, now,
            null, JsonSerializer.Serialize(new { DueSettingCount = due.Count, ScheduledCount = scheduled })));
        await db.SaveChangesAsync();
        return Results.Ok(new { dueSettingCount = due.Count, scheduledCount = scheduled });
    }

    private static async Task<Response> ToResponseAsync(InstrumentMaintenanceSetting setting, AbderaDbContext db)
    {
        var instrumentName = await db.Instruments.Where(item => item.Id == setting.InstrumentId).Select(item => item.Name).SingleAsync();
        var consentingCount = await (
            from enrollment in db.Enrollments
            join link in db.StudentGuardians on enrollment.StudentId equals link.StudentId
            join guardian in db.Guardians on link.GuardianId equals guardian.Id
            where enrollment.InstrumentId == setting.InstrumentId && enrollment.Status == EnrollmentStatus.Active && guardian.NotificationConsent
            select guardian.Id).Distinct().CountAsync();
        return new Response(setting.Id, setting.InstrumentId, instrumentName, setting.MaintenanceType, setting.PeriodDays,
            setting.IsEnabled, setting.NotificationPreference, setting.NextReminderAt, consentingCount);
    }
}
