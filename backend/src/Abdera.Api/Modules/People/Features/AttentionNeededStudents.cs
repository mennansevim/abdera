using System.Security.Claims;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

public static class AttentionNeededStudents
{
    public record Response(Guid StudentId, string StudentName, int RecentAbsenceCount, List<string> Reasons);

    public static void MapAttentionNeededStudents(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/students/attention-needed", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var teacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        var cutoff = clock.UtcNow.AddDays(-30);
        var query =
            from attendance in db.LessonAttendances
            join lesson in db.Lessons on attendance.LessonId equals lesson.Id
            join student in db.Students on lesson.StudentId equals student.Id
            where attendance.Status == AttendanceStatus.Absent && lesson.StartAt >= cutoff
            select new { lesson.StudentId, StudentName = student.FirstName + " " + student.LastName, lesson.TeacherId };
        if (teacherId is { } scopedTeacherId)
            query = query.Where(row => row.TeacherId == scopedTeacherId);

        var counts = await query
            .GroupBy(row => new { row.StudentId, row.StudentName })
            .Select(group => new { group.Key.StudentId, group.Key.StudentName, Count = group.Count() })
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.StudentName)
            .ToListAsync();

        var result = counts
            .Select(row => new { row, Signal = AttentionSignal.Evaluate(row.Count, 0) })
            .Where(item => item.Signal.NeedsAttention)
            .Select(item => new Response(item.row.StudentId, item.row.StudentName, item.row.Count, item.Signal.Reasons.ToList()))
            .ToList();
        return Results.Ok(result);
    }
}
