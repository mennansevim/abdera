using System.Security.Claims;
using Abdera.Api.Modules.People.Domain;
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
    }

    private static async Task<IResult> CreateAsync(Guid studentId, CreateRequest request, AbderaDbContext db, IClock clock)
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

    private static EnrollmentResponse ToResponse(Enrollment e) =>
        new(e.Id, e.StudentId, e.TeacherId, e.InstrumentId, e.Status, e.StartedAt, e.EndedAt);
}
