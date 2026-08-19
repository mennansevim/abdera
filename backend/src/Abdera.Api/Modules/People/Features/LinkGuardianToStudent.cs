using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// docs/07-api.md'de açıkça listelenmemiş ama People modülünün temel ilişkisi bu olmadan
// kurulamaz - bir öğrenci ile veliyi ilişkilendirir (docs/03-erd.md student_guardians).
public static class LinkGuardianToStudent
{
    public record Request(Guid GuardianId, string? Relationship, bool IsPrimary);
    public record StudentGuardianResponse(Guid StudentId, Guid GuardianId, string? Relationship, bool IsPrimary);

    public static void MapLinkGuardianToStudent(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/students/{studentId:guid}/guardians", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        app.MapGet("/api/students/{studentId:guid}/guardians", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> HandleAsync(Guid studentId, Request request, AbderaDbContext db)
    {
        if (!await db.Students.AnyAsync(s => s.Id == studentId))
            throw new NotFoundException("Öğrenci bulunamadı.");
        if (!await db.Guardians.AnyAsync(g => g.Id == request.GuardianId))
            throw new NotFoundException("Veli bulunamadı.");
        if (await db.StudentGuardians.AnyAsync(sg => sg.StudentId == studentId && sg.GuardianId == request.GuardianId))
            throw new ConflictException("Bu veli zaten bu öğrenciyle ilişkilendirilmiş.");

        var link = StudentGuardian.Create(studentId, request.GuardianId, request.Relationship, request.IsPrimary);
        db.StudentGuardians.Add(link);
        await db.SaveChangesAsync();

        return Results.Created(
            $"/api/students/{studentId}/guardians",
            new StudentGuardianResponse(studentId, request.GuardianId, link.Relationship, link.IsPrimary));
    }

    private static async Task<IResult> ListAsync(Guid studentId, AbderaDbContext db)
    {
        var guardians = await db.StudentGuardians
            .Where(sg => sg.StudentId == studentId)
            .Join(db.Guardians, sg => sg.GuardianId, g => g.Id, (sg, g) =>
                new { g.Id, g.FirstName, g.LastName, g.PhoneNumber, sg.Relationship, sg.IsPrimary })
            .ToListAsync();

        return Results.Ok(guardians);
    }
}
