using System.Security.Claims;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// docs/07-api.md GET /api/students/{studentId}/makeup-credits, POST /api/makeup-credits/{id}/use
// (A2). "Kullanma" = yeni bir MAKEUP dersi planlamak - lesson_series_id yok (ERD notu).
public static class MakeupCredits
{
    public record CreditResponse(
        Guid Id, Guid StudentId, MakeupCreditEarnedReason EarnedReason,
        DateTimeOffset EarnedAt, DateTimeOffset ExpiresAt, MakeupCreditStatus Status, Guid? UsedLessonId);

    public record UseRequest(Guid TeacherId, Guid InstrumentId, DateTimeOffset StartAt, int DurationMinutes);
    public record UseResponse(Guid CreditId, Guid NewLessonId);

    public static void MapMakeupCredits(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/students/{studentId:guid}/makeup-credits", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        app.MapPost("/api/makeup-credits/{creditId:guid}/use", UseAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(Guid studentId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is { } teacherId)
        {
            var isAssigned = await db.Enrollments.AnyAsync(e => e.StudentId == studentId && e.TeacherId == teacherId);
            if (!isAssigned) throw new ForbiddenException("Bu öğrenci size atanmamış.");
        }

        var credits = await db.MakeupCredits.Where(c => c.StudentId == studentId)
            .OrderByDescending(c => c.EarnedAt)
            .Select(c => new CreditResponse(c.Id, c.StudentId, c.EarnedReason, c.EarnedAt, c.ExpiresAt, c.Status, c.UsedLessonId))
            .ToListAsync();

        return Results.Ok(credits);
    }

    private static async Task<IResult> UseAsync(
        Guid creditId, UseRequest request, AbderaDbContext db, IClock clock, INotificationScheduler scheduler)
    {
        if (request.DurationMinutes <= 0)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["durationMinutes"] = ["Süre pozitif olmalı."] });

        var credit = await db.MakeupCredits.SingleOrDefaultAsync(c => c.Id == creditId)
            ?? throw new NotFoundException("Telafi kredisi bulunamadı.");

        if (!await db.Teachers.AnyAsync(t => t.Id == request.TeacherId))
            throw new NotFoundException("Öğretmen bulunamadı.");
        if (!await db.Instruments.AnyAsync(i => i.Id == request.InstrumentId))
            throw new NotFoundException("Enstrüman bulunamadı.");

        var endAt = request.StartAt.AddMinutes(request.DurationMinutes);
        var hasConflict = await LessonConflictChecker.HasOverlapAsync(db, request.TeacherId, credit.StudentId, request.StartAt, endAt);
        if (hasConflict)
            throw new ConflictException("Bu saat, öğretmenin veya öğrencinin başka bir dersiyle çakışıyor.");

        var makeupLesson = Lesson.CreateMakeup(credit.StudentId, request.TeacherId, request.InstrumentId, request.StartAt, endAt, clock.UtcNow);
        db.Lessons.Add(makeupLesson);

        credit.Use(makeupLesson.Id, clock.UtcNow);

        var primaryGuardianId = await PrimaryGuardianResolver.ResolveAsync(db, credit.StudentId);
        if (primaryGuardianId is { } guardianId)
        {
            await scheduler.ScheduleAsync(NotificationJobType.MakeupApproved, "lesson", makeupLesson.Id, guardianId, clock.UtcNow);
        }

        await db.SaveChangesAsync();
        return Results.Ok(new UseResponse(credit.Id, makeupLesson.Id));
    }
}
