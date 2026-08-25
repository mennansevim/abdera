using System.Security.Claims;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Progress.Features;

public static class PracticeAssignments
{
    public record CreateRequest(string Description, DateOnly? DueDate);
    public record AssignmentResponse(
        Guid Id,
        Guid LessonId,
        string Description,
        DateOnly? DueDate,
        bool Completed,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public static void MapPracticeAssignments(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lessons/{lessonId:guid}/practice-assignments", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/lessons/{lessonId:guid}/practice-assignments", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPatch("/api/practice-assignments/{assignmentId:guid}/complete", CompleteAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> ListAsync(
        Guid lessonId,
        ClaimsPrincipal principal,
        AbderaDbContext db)
    {
        await ProgressAuthorization.EnsureLessonAccessAsync(lessonId, principal, db);
        var assignments = await db.PracticeAssignments
            .Where(item => item.LessonId == lessonId)
            .OrderBy(item => item.Completed)
            .ThenBy(item => item.DueDate)
            .ThenBy(item => item.CreatedAt)
            .Select(item => new AssignmentResponse(
                item.Id,
                item.LessonId,
                item.Description,
                item.DueDate,
                item.Completed,
                item.CreatedAt,
                item.UpdatedAt))
            .ToListAsync();
        return Results.Ok(assignments);
    }

    private static async Task<IResult> CreateAsync(
        Guid lessonId,
        CreateRequest request,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock)
    {
        if (AuthContext.IsAdmin(principal))
            throw new ForbiddenException("Çalışma ödevini yalnızca öğretmen girebilir.");
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                [nameof(request.Description)] = ["Çalışma açıklaması boş olamaz."],
            });
        }

        await ProgressAuthorization.EnsureLessonAccessAsync(lessonId, principal, db);
        var assignment = PracticeAssignment.Create(lessonId, request.Description, request.DueDate, clock.UtcNow);
        db.PracticeAssignments.Add(assignment);
        await db.SaveChangesAsync();
        return Results.Created($"/api/practice-assignments/{assignment.Id}", ToResponse(assignment));
    }

    private static async Task<IResult> CompleteAsync(
        Guid assignmentId,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock)
    {
        if (AuthContext.IsAdmin(principal))
            throw new ForbiddenException("Çalışma ödevini yalnızca öğretmen tamamlayabilir.");
        var assignment = await db.PracticeAssignments.SingleOrDefaultAsync(item => item.Id == assignmentId)
            ?? throw new NotFoundException("Çalışma ödevi bulunamadı.");
        await ProgressAuthorization.EnsureLessonAccessAsync(assignment.LessonId, principal, db);
        assignment.MarkCompleted(clock.UtcNow);
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(assignment));
    }

    private static AssignmentResponse ToResponse(PracticeAssignment assignment) => new(
        assignment.Id,
        assignment.LessonId,
        assignment.Description,
        assignment.DueDate,
        assignment.Completed,
        assignment.CreatedAt,
        assignment.UpdatedAt);
}
