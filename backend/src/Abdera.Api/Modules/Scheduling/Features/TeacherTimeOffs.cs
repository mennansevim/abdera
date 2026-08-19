using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// docs/07-api.md /api/teachers/{teacherId}/time-off. docs/10-decisions.md A3 - ders üretimi
// bu aralıklara denk gelen occurrence'ları atlar (bkz. Modules/Scheduling/Domain/LessonGenerator.cs).
public static class TeacherTimeOffs
{
    public record CreateRequest(DateOnly StartsOn, DateOnly EndsOn, string? Reason);
    public record TimeOffResponse(Guid Id, DateOnly StartsOn, DateOnly EndsOn, string? Reason);

    public static void MapTeacherTimeOffs(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/teachers/{teacherId:guid}/time-off", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        app.MapPost("/api/teachers/{teacherId:guid}/time-off", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(Guid teacherId, AbderaDbContext db)
    {
        var items = await db.TeacherTimeOffs
            .Where(t => t.TeacherId == teacherId)
            .OrderByDescending(t => t.StartsOn)
            .Select(t => new TimeOffResponse(t.Id, t.StartsOn, t.EndsOn, t.Reason))
            .ToListAsync();

        return Results.Ok(items);
    }

    private static async Task<IResult> CreateAsync(Guid teacherId, CreateRequest request, AbderaDbContext db, IClock clock)
    {
        if (!await db.Teachers.AnyAsync(t => t.Id == teacherId))
            throw new NotFoundException("Öğretmen bulunamadı.");

        var timeOff = TeacherTimeOff.Create(teacherId, request.StartsOn, request.EndsOn, request.Reason, clock.UtcNow);
        db.TeacherTimeOffs.Add(timeOff);
        await db.SaveChangesAsync();

        return Results.Created(
            $"/api/teachers/{teacherId}/time-off/{timeOff.Id}",
            new TimeOffResponse(timeOff.Id, timeOff.StartsOn, timeOff.EndsOn, timeOff.Reason));
    }
}
