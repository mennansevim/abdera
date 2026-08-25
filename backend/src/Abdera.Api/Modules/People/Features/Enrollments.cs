using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// docs/07-api.md'de ayrı bir /api/enrollments yok - Enrollment her zaman bir öğrenciye
// bağlı olduğu için öğrenci altında iç içe (nested resource) sunuluyor.
public static class Enrollments
{
    public record CreateRequest(Guid TeacherId, Guid InstrumentId, DateOnly StartedAt);
    public record EnrollmentResponse(
        Guid Id, Guid StudentId, Guid TeacherId, Guid InstrumentId,
        EnrollmentStatus Status, DateOnly StartedAt, DateOnly? EndedAt);

    public static void MapEnrollments(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/students/{studentId:guid}/enrollments", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        app.MapGet("/api/students/{studentId:guid}/enrollments", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        app.MapDelete("/api/students/{studentId:guid}/enrollments/{enrollmentId:guid}", EndAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> CreateAsync(Guid studentId, CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        if (!await db.Students.AnyAsync(s => s.Id == studentId))
            throw new NotFoundException("Öğrenci bulunamadı.");

        var teacher = await db.Teachers.SingleOrDefaultAsync(t => t.Id == request.TeacherId)
            ?? throw new NotFoundException("Öğretmen bulunamadı.");
        if (teacher.Status != TeacherStatus.Active)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["teacherId"] = ["Bu öğretmen aktif değil."],
            });

        if (!await db.Instruments.AnyAsync(i => i.Id == request.InstrumentId))
            throw new NotFoundException("Enstrüman bulunamadı.");

        var teacherTeachesInstrument = await db.TeacherInstruments
            .AnyAsync(ti => ti.TeacherId == request.TeacherId && ti.InstrumentId == request.InstrumentId);
        if (!teacherTeachesInstrument)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["instrumentId"] = ["Bu öğretmen bu enstrümanı öğretmiyor."],
            });

        var alreadyEnrolled = await db.Enrollments.AnyAsync(e =>
            e.StudentId == studentId && e.TeacherId == request.TeacherId &&
            e.InstrumentId == request.InstrumentId && e.Status == EnrollmentStatus.Active);
        if (alreadyEnrolled)
            throw new ConflictException("Öğrenci bu öğretmen ve enstrüman için zaten aktif bir kayda sahip.");

        var enrollment = Enrollment.Create(studentId, request.TeacherId, request.InstrumentId, request.StartedAt, clock.UtcNow);
        db.Enrollments.Add(enrollment);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "enrollment.created",
            nameof(Enrollment),
            enrollment.Id,
            clock.UtcNow,
            afterJson: JsonSerializer.Serialize(new
            {
                enrollment.StudentId,
                enrollment.TeacherId,
                enrollment.InstrumentId,
                enrollment.StartedAt,
                Status = enrollment.Status.ToString(),
            })));
        await db.SaveChangesAsync();

        return Results.Created($"/api/students/{studentId}/enrollments/{enrollment.Id}", ToResponse(enrollment));
    }

    private static async Task<IResult> ListAsync(Guid studentId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);

        var query = db.Enrollments.Where(e => e.StudentId == studentId);
        if (teacherScope is { } teacherId)
        {
            query = query.Where(e => e.TeacherId == teacherId);
        }

        var enrollments = await query.OrderBy(e => e.StartedAt).ToListAsync();
        return Results.Ok(enrollments.Select(ToResponse));
    }

    private static async Task<IResult> EndAsync(
        Guid studentId, Guid enrollmentId, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var enrollment = await db.Enrollments.SingleOrDefaultAsync(e => e.Id == enrollmentId && e.StudentId == studentId)
            ?? throw new NotFoundException("Kurs kaydı bulunamadı.");

        if (enrollment.Status == EnrollmentStatus.Ended)
            return Results.NoContent();

        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);
        enrollment.End(today < enrollment.StartedAt ? enrollment.StartedAt : today, now);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "enrollment.ended",
            nameof(Enrollment),
            enrollment.Id,
            now,
            JsonSerializer.Serialize(new { Status = EnrollmentStatus.Active.ToString(), EndedAt = (DateOnly?)null }),
            JsonSerializer.Serialize(new { Status = enrollment.Status.ToString(), enrollment.EndedAt })));

        var series = await db.LessonSeries
            .Where(item => item.EnrollmentId == enrollmentId && item.Status == LessonSeriesStatus.Active)
            .ToListAsync();
        foreach (var item in series)
        {
            var endDate = today < item.EffectiveFrom ? item.EffectiveFrom : today;
            item.EndAs(endDate, now);
        }

        var seriesIds = series.Select(item => item.Id).ToList();
        if (seriesIds.Count > 0)
        {
            var futureLessons = await db.Lessons
                .Where(lesson => lesson.LessonSeriesId.HasValue && seriesIds.Contains(lesson.LessonSeriesId.Value))
                .Where(lesson => lesson.StartAt > now && lesson.Status == LessonStatus.Normal)
                .ToListAsync();
            var futureLessonIds = futureLessons.Select(lesson => lesson.Id).ToList();
            if (futureLessonIds.Count > 0)
            {
                var pendingJobs = await db.NotificationJobs
                    .Where(job => job.ReferenceType == "lesson" && futureLessonIds.Contains(job.ReferenceId))
                    .Where(job => job.Status == NotificationJobStatus.Pending || job.Status == NotificationJobStatus.Processing)
                    .ToListAsync();
                foreach (var job in pendingJobs) job.Cancel(now);
                db.Lessons.RemoveRange(futureLessons);
            }
        }

        var activeFeePlans = await db.FeePlans
            .Where(plan => plan.EnrollmentId == enrollmentId && plan.ActiveUntil == null)
            .ToListAsync();
        foreach (var plan in activeFeePlans)
        {
            plan.End(today < plan.ActiveFrom ? plan.ActiveFrom : today);
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static EnrollmentResponse ToResponse(Enrollment e) =>
        new(e.Id, e.StudentId, e.TeacherId, e.InstrumentId, e.Status, e.StartedAt, e.EndedAt);
}
