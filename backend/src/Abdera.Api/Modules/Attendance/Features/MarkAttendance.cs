using System.Security.Claims;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Attendance.Features;

// docs/07-api.md POST /api/lessons/{lessonId}/attendance. docs/04-permissions.md: Teacher
// yalnızca kendi dersi, Admin override edebilir ama audit'e düşer. docs/05-state-models.md:
// kayıt tek yönlü - ilk girişte oluşur, sonrasında düzeltme olarak güncellenir.
public static class MarkAttendance
{
    public record MarkRequest(AttendanceStatus Status, string? Note);
    public record AttendanceResponse(Guid Id, Guid LessonId, AttendanceStatus Status, Guid MarkedByTeacherId, DateTimeOffset MarkedAt, string? Note);

    public static void MapMarkAttendance(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lessons/{lessonId:guid}/attendance", GetAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/lessons/{lessonId:guid}/attendance", MarkAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> GetAsync(Guid lessonId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(l => l.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");
        await EnsureTeacherOwnsLessonAsync(lesson.TeacherId, principal, db);

        var attendance = await db.LessonAttendances.SingleOrDefaultAsync(a => a.LessonId == lessonId);
        return attendance is null ? Results.NotFound() : Results.Ok(ToResponse(attendance));
    }

    private static async Task<IResult> MarkAsync(Guid lessonId, MarkRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(l => l.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");

        var isAdmin = AuthContext.IsAdmin(principal);
        await EnsureTeacherOwnsLessonAsync(lesson.TeacherId, principal, db);

        var existing = await db.LessonAttendances.SingleOrDefaultAsync(a => a.LessonId == lessonId);
        var actorUserId = AuthContext.GetUserId(principal);

        if (existing is null)
        {
            // İlk kayıt her zaman dersin öğretmenine ait olarak işlenir - Admin override bile
            // etse "kim işledi" alanı ders sahibini gösterir; audit'te gerçek aktör görünür.
            var attendance = LessonAttendance.Create(lessonId, request.Status, lesson.TeacherId, request.Note, clock.UtcNow);
            db.LessonAttendances.Add(attendance);
            lesson.Complete(clock.UtcNow);

            if (isAdmin)
            {
                db.AuditLogs.Add(AuditLog.Record(actorUserId, "lesson.attendance_marked_by_admin", nameof(LessonAttendance), attendance.Id, clock.UtcNow));
            }

            await db.SaveChangesAsync();
            return Results.Created($"/api/lessons/{lessonId}/attendance", ToResponse(attendance));
        }

        var before = $"{{\"status\":\"{existing.Status}\",\"note\":{(existing.Note is null ? "null" : $"\"{existing.Note}\"")}}}";
        existing.Correct(request.Status, lesson.TeacherId, request.Note, clock.UtcNow);
        db.AuditLogs.Add(AuditLog.Record(actorUserId, "lesson.attendance_corrected", nameof(LessonAttendance), existing.Id, clock.UtcNow, beforeJson: before));

        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(existing));
    }

    private static async Task EnsureTeacherOwnsLessonAsync(Guid lessonTeacherId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is { } teacherId && teacherId != lessonTeacherId)
        {
            throw new ForbiddenException("Bu ders size atanmamış.");
        }
    }

    private static AttendanceResponse ToResponse(LessonAttendance a) =>
        new(a.Id, a.LessonId, a.Status, a.MarkedByTeacherId, a.MarkedAt, a.Note);
}
