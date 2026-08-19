using System.Security.Claims;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// docs/07-api.md /api/students. docs/04-permissions.md: Admin tümünü görür/düzenler;
// Teacher yalnızca kendi Enrollment'ı olan (kendisine atanmış) öğrencileri görür, düzenleyemez.
public static class Students
{
    public record CreateRequest(string FirstName, string LastName, DateOnly BirthDate);
    public record UpdateRequest(string FirstName, string LastName, DateOnly BirthDate, StudentStatus Status);
    public record StudentResponse(Guid Id, string FirstName, string LastName, DateOnly BirthDate, StudentStatus Status);

    public static void MapStudents(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/students").RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        group.MapGet("", ListAsync);
        group.MapGet("/{studentId:guid}", GetAsync);
        group.MapPost("", CreateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapPatch("/{studentId:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, AbderaDbContext db)
    {
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);

        var query = db.Students.AsQueryable();
        if (teacherScope is { } teacherId)
        {
            var assignedStudentIds = db.Enrollments.Where(e => e.TeacherId == teacherId).Select(e => e.StudentId);
            query = query.Where(s => assignedStudentIds.Contains(s.Id));
        }

        var students = await query
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Select(s => new StudentResponse(s.Id, s.FirstName, s.LastName, s.BirthDate, s.Status))
            .ToListAsync();

        return Results.Ok(students);
    }

    private static async Task<IResult> GetAsync(Guid studentId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var student = await db.Students.SingleOrDefaultAsync(s => s.Id == studentId)
            ?? throw new NotFoundException("Öğrenci bulunamadı.");

        await EnsureTeacherCanAccessAsync(studentId, principal, db);

        return Results.Ok(new StudentResponse(student.Id, student.FirstName, student.LastName, student.BirthDate, student.Status));
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, AbderaDbContext db, IClock clock)
    {
        var student = Student.Create(request.FirstName, request.LastName, request.BirthDate, clock.UtcNow);
        db.Students.Add(student);
        await db.SaveChangesAsync();

        return Results.Created($"/api/students/{student.Id}",
            new StudentResponse(student.Id, student.FirstName, student.LastName, student.BirthDate, student.Status));
    }

    private static async Task<IResult> UpdateAsync(Guid studentId, UpdateRequest request, AbderaDbContext db, IClock clock)
    {
        var student = await db.Students.SingleOrDefaultAsync(s => s.Id == studentId)
            ?? throw new NotFoundException("Öğrenci bulunamadı.");

        student.Update(request.FirstName, request.LastName, request.BirthDate, clock.UtcNow);
        student.SetStatus(request.Status, clock.UtcNow);
        await db.SaveChangesAsync();

        return Results.Ok(new StudentResponse(student.Id, student.FirstName, student.LastName, student.BirthDate, student.Status));
    }

    private static async Task EnsureTeacherCanAccessAsync(Guid studentId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is null) return; // Admin

        var isAssigned = await db.Enrollments.AnyAsync(e => e.StudentId == studentId && e.TeacherId == teacherScope);
        if (!isAssigned)
        {
            throw new ForbiddenException("Bu öğrenci size atanmamış.");
        }
    }
}
