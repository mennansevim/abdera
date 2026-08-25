using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// docs/00-master-prompt.md "Lesson change" akışı: Teacher veya Admin talep açar -> Admin
// onaylar/reddeder -> onaylanırsa Lesson.CreateRescheduled ile geçmiş korunarak yeni satır
// açılır. docs/04-permissions.md: talep açma Teacher(kendi dersi)/Admin, karar yalnızca Admin.
public static class ChangeRequests
{
    public record CreateRequest(string? Reason, DateTimeOffset ProposedStartAt, DateTimeOffset ProposedEndAt);

    public record ChangeRequestResponse(
        Guid Id, Guid LessonId, Guid RequestedBy, string? Reason,
        DateTimeOffset ProposedStartAt, DateTimeOffset ProposedEndAt,
        LessonChangeRequestStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt);

    public record ApproveResponse(ChangeRequestResponse Request, Guid NewLessonId);

    public static void MapChangeRequests(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/lessons/{lessonId:guid}/change-requests", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        app.MapGet("/api/change-requests", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        app.MapPost("/api/change-requests/{requestId:guid}/approve", ApproveAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        app.MapPost("/api/change-requests/{requestId:guid}/reject", RejectAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> CreateAsync(
        Guid lessonId, CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(l => l.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");

        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is { } teacherId && teacherId != lesson.TeacherId)
            throw new ForbiddenException("Bu ders size atanmamış.");

        if (lesson.Status != LessonStatus.Normal)
            throw new ConflictException($"'{lesson.Status}' durumundaki bir ders için değişiklik talebi açılamaz.");

        var changeRequest = LessonChangeRequest.Create(
            lessonId, AuthContext.GetUserId(principal), request.Reason,
            request.ProposedStartAt, request.ProposedEndAt, clock.UtcNow);

        db.LessonChangeRequests.Add(changeRequest);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "lesson.change_requested",
            nameof(LessonChangeRequest),
            changeRequest.Id,
            clock.UtcNow,
            afterJson: JsonSerializer.Serialize(new
            {
                changeRequest.LessonId,
                changeRequest.ProposedStartAt,
                changeRequest.ProposedEndAt,
            })));
        await db.SaveChangesAsync();

        return Results.Created($"/api/change-requests/{changeRequest.Id}", ToResponse(changeRequest));
    }

    private static async Task<IResult> ListAsync(LessonChangeRequestStatus? status, AbderaDbContext db)
    {
        var query = db.LessonChangeRequests.AsQueryable();
        if (status is { } s) query = query.Where(r => r.Status == s);

        var items = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        return Results.Ok(items.Select(ToResponse));
    }

    private static async Task<IResult> ApproveAsync(
        Guid requestId, ClaimsPrincipal principal, AbderaDbContext db, IClock clock, INotificationScheduler scheduler)
    {
        var changeRequest = await db.LessonChangeRequests.SingleOrDefaultAsync(r => r.Id == requestId)
            ?? throw new NotFoundException("Değişiklik talebi bulunamadı.");
        var lesson = await db.Lessons.SingleAsync(l => l.Id == changeRequest.LessonId);

        var hasConflict = await LessonConflictChecker.HasOverlapAsync(
            db, lesson.TeacherId, lesson.StudentId, changeRequest.ProposedStartAt, changeRequest.ProposedEndAt, excludeLessonId: lesson.Id);
        if (hasConflict)
            throw new ConflictException("Önerilen saat, öğretmenin veya öğrencinin başka bir dersiyle çakışıyor.");

        var newLesson = Lesson.CreateRescheduled(lesson, changeRequest.ProposedStartAt, changeRequest.ProposedEndAt, clock.UtcNow);
        db.Lessons.Add(newLesson);
        changeRequest.Approve(clock.UtcNow);

        // docs/10-decisions.md A4: eski derse bağlı bekleyen hatırlatma iptal edilir, yeni
        // saate göre yenisi kurulur; ayrıca veliye "ders ertelendi" bilgisi anında gönderilir.
        await scheduler.CancelPendingAsync("lesson", lesson.Id);

        var primaryGuardianId = await PrimaryGuardianResolver.ResolveAsync(db, lesson.StudentId);
        if (primaryGuardianId is { } guardianId)
        {
            var automationSettings = await NotificationAutomationSettings.GetCurrentAsync(db);
            var reminderMinutesBefore = automationSettings.LessonReminderMinutesBefore;
            await scheduler.ScheduleAsync(
                NotificationJobType.LessonReminder, "lesson", newLesson.Id, guardianId,
                newLesson.StartAt.AddMinutes(-reminderMinutesBefore));
            await scheduler.ScheduleAsync(
                NotificationJobType.LessonRescheduled, "lesson", newLesson.Id, guardianId, clock.UtcNow);
        }

        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "lesson.change_approved",
            nameof(LessonChangeRequest),
            changeRequest.Id,
            clock.UtcNow,
            JsonSerializer.Serialize(new
            {
                lesson.StudentId,
                lesson.TeacherId,
                lesson.StartAt,
                lesson.EndAt,
                Status = LessonStatus.Normal.ToString(),
            }),
            JsonSerializer.Serialize(new
            {
                NewLessonId = newLesson.Id,
                newLesson.StartAt,
                newLesson.EndAt,
                Status = newLesson.Status.ToString(),
            })));

        await db.SaveChangesAsync();
        return Results.Ok(new ApproveResponse(ToResponse(changeRequest), newLesson.Id));
    }

    private static async Task<IResult> RejectAsync(Guid requestId, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var changeRequest = await db.LessonChangeRequests.SingleOrDefaultAsync(r => r.Id == requestId)
            ?? throw new NotFoundException("Değişiklik talebi bulunamadı.");

        changeRequest.Reject(clock.UtcNow);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "lesson.change_rejected",
            nameof(LessonChangeRequest),
            changeRequest.Id,
            clock.UtcNow));
        await db.SaveChangesAsync();

        return Results.Ok(ToResponse(changeRequest));
    }

    private static ChangeRequestResponse ToResponse(LessonChangeRequest r) => new(
        r.Id, r.LessonId, r.RequestedBy, r.Reason, r.ProposedStartAt, r.ProposedEndAt, r.Status, r.CreatedAt, r.ResolvedAt);
}
