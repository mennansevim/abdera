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
    public record CreateRequest(Guid TeacherId, Guid InstrumentId, DateOnly StartedAt, CourseKind? CourseKind = null);
    // Aidat tutarını etkileyen iki alan (ders türü ve elle indirim) kayıt açıldıktan sonra
    // da değiştirilebilir - yeni bir kayıt açmak geçmiş aidatları kopardığı için doğru yol değil.
    public record UpdateRequest(CourseKind? CourseKind, decimal? ManualDiscountPercent, string? ManualDiscountReason);
    public record EnrollmentResponse(
        Guid Id, Guid StudentId, Guid TeacherId, Guid InstrumentId, CourseKind CourseKind,
        decimal? ManualDiscountPercent, string? ManualDiscountReason,
        EnrollmentStatus Status, DateOnly StartedAt, DateOnly? EndedAt);

    public static void MapEnrollments(this IEndpointRouteBuilder app)
    {
        // Öğretmen kendi öğrencisine yeni bir kurs açabilir ama yalnızca KENDİ adına (J1).
        app.MapPost("/api/students/{studentId:guid}/enrollments", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        app.MapGet("/api/students/{studentId:guid}/enrollments", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        app.MapPatch("/api/students/{studentId:guid}/enrollments/{enrollmentId:guid}", UpdateAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        app.MapDelete("/api/students/{studentId:guid}/enrollments/{enrollmentId:guid}", EndAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> CreateAsync(Guid studentId, CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        if (!await db.Students.AnyAsync(s => s.Id == studentId))
            throw new NotFoundException("Öğrenci bulunamadı.");

        // İki ayrı kontrol, ikisi de gerekli:
        //  1. Öğretmen başkası adına kurs açamaz (istekteki teacherId kendisi olmalı).
        //  2. Öğretmen yalnızca ZATEN kendi öğrencisi olan birine yeni kurs açabilir.
        // İkincisi olmadan bir öğretmen, kendini herhangi bir öğrencinin öğretmeni yazarak
        // o öğrencinin verisine erişebilirdi - testle yakalanan gerçek bir açıktı.
        var scopedTeacherId = await PeopleAuthorization.EnsureActsAsSelfAsync(request.TeacherId, principal, db);
        if (scopedTeacherId is not null)
        {
            await PeopleAuthorization.EnsureStudentAccessAsync(studentId, principal, db);
        }

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

        var enrollment = Enrollment.Create(
            studentId, request.TeacherId, request.InstrumentId,
            request.CourseKind ?? People.Domain.CourseKind.Individual, request.StartedAt, clock.UtcNow);
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
                CourseKind = enrollment.CourseKind.ToString(),
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

        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateAsync(
        Guid studentId, Guid enrollmentId, UpdateRequest request, ClaimsPrincipal principal,
        AbderaDbContext db, IClock clock)
    {
        var enrollment = await db.Enrollments
            .SingleOrDefaultAsync(e => e.Id == enrollmentId && e.StudentId == studentId)
            ?? throw new NotFoundException("Kurs kaydı bulunamadı.");

        var now = clock.UtcNow;
        var before = JsonSerializer.Serialize(new
        {
            CourseKind = enrollment.CourseKind.ToString(),
            enrollment.ManualDiscountPercent,
            enrollment.ManualDiscountReason,
        });

        if (request.CourseKind is { } courseKind) enrollment.SetCourseKind(courseKind, now);
        enrollment.SetManualDiscount(request.ManualDiscountPercent, request.ManualDiscountReason, now);

        // Aidat tutarını değiştiren bir karar - CLAUDE.md: parayı etkileyen her use-case
        // audit_log'a kim/ne zaman/önceki/yeni değeriyle yazar.
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal), "enrollment.billing_updated", nameof(Enrollment), enrollment.Id, now,
            beforeJson: before,
            afterJson: JsonSerializer.Serialize(new
            {
                CourseKind = enrollment.CourseKind.ToString(),
                enrollment.ManualDiscountPercent,
                enrollment.ManualDiscountReason,
            })));

        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(enrollment));
    }

    private static EnrollmentResponse ToResponse(Enrollment e) =>
        new(e.Id, e.StudentId, e.TeacherId, e.InstrumentId, e.CourseKind,
            e.ManualDiscountPercent, e.ManualDiscountReason, e.Status, e.StartedAt, e.EndedAt);
}
