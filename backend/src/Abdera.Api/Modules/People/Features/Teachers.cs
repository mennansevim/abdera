using Abdera.Api.Modules.Auth.Domain;
using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// docs/07-api.md /api/teachers. docs/04-permissions.md: liste her iki role de açık
// (isim/enstrüman - kişisel veri yok), oluşturma/düzenleme yalnızca Admin.
public static class Teachers
{
    // email verilirse öğretmen için bir giriş hesabı da açılır (docs/10-decisions.md B4 -
    // yönetici geçici şifre üretir, öğretmen ilk girişte kalıcısını belirler).
    public record CreateRequest(string FirstName, string LastName, Guid[] InstrumentIds, string? Email);
    public record UpdateRequest(string FirstName, string LastName, TeacherStatus Status, Guid[] InstrumentIds);
    public record TeacherResponse(Guid Id, string FirstName, string LastName, TeacherStatus Status, Guid[] InstrumentIds, bool HasLoginAccount);
    public record CreateResponse(TeacherResponse Teacher, string? TemporaryPassword);
    public record TeacherStudentResponse(
        Guid StudentId, string FirstName, string LastName, Guid EnrollmentId,
        Guid InstrumentId, string InstrumentName, DateOnly StartedAt);
    public record TeacherOverviewResponse(TeacherResponse Teacher, List<TeacherStudentResponse> Students);
    public record CreateStudentRequest(
        string FirstName, string LastName, DateOnly BirthDate, Guid InstrumentId, DateOnly StartedAt);

    public static void MapTeachers(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/teachers").RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        group.MapGet("", ListAsync);
        group.MapGet("/overview", OverviewAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapPost("", CreateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapPost("/{teacherId:guid}/students", CreateStudentAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapPatch("/{teacherId:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db)
    {
        var teachers = await LoadTeacherResponsesAsync(db, t => true);
        return Results.Ok(teachers);
    }

    private static async Task<IResult> OverviewAsync(AbderaDbContext db)
    {
        var teachers = await LoadTeacherResponsesAsync(db, teacher => true);
        var teacherIds = teachers.Select(teacher => teacher.Id).ToList();
        var students = await db.Enrollments
            .Where(enrollment => teacherIds.Contains(enrollment.TeacherId) && enrollment.Status == EnrollmentStatus.Active)
            .Join(db.Students, enrollment => enrollment.StudentId, student => student.Id, (enrollment, student) => new { enrollment, student })
            .Join(db.Instruments, item => item.enrollment.InstrumentId, instrument => instrument.Id, (item, instrument) => new
            {
                item.enrollment.TeacherId,
                Student = new TeacherStudentResponse(
                    item.student.Id, item.student.FirstName, item.student.LastName, item.enrollment.Id,
                    instrument.Id, instrument.Name, item.enrollment.StartedAt),
            })
            .ToListAsync();

        return Results.Ok(teachers.Select(teacher => new TeacherOverviewResponse(
            teacher,
            students.Where(item => item.TeacherId == teacher.Id)
                .Select(item => item.Student)
                .OrderBy(student => student.LastName)
                .ThenBy(student => student.FirstName)
                .ToList())));
    }

    private static async Task<IResult> CreateAsync(
        CreateRequest request, AbderaDbContext db, IPasswordHasher<User> passwordHasher, IClock clock)
    {
        var instruments = await db.Instruments.Where(i => request.InstrumentIds.Contains(i.Id)).ToListAsync();
        if (instruments.Count != request.InstrumentIds.Distinct().Count())
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["instrumentIds"] = ["Bir veya daha fazla enstrüman bulunamadı."],
            });

        string? temporaryPassword = null;
        Guid? userId = null;

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = request.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email))
                throw new ConflictException("Bu e-posta ile kayıtlı bir kullanıcı zaten var.");

            var user = User.Create(email, "placeholder", UserRole.Teacher, clock.UtcNow, mustChangePassword: true);
            temporaryPassword = TemporaryPasswordGenerator.Generate();
            user.SetPassword(passwordHasher.HashPassword(user, temporaryPassword), clock.UtcNow, mustChangePassword: true);
            db.Users.Add(user);
            userId = user.Id;
        }

        var teacher = Teacher.Create(request.FirstName, request.LastName, clock.UtcNow, userId);
        db.Teachers.Add(teacher);
        foreach (var instrument in instruments)
        {
            db.TeacherInstruments.Add(TeacherInstrument.Create(teacher.Id, instrument.Id));
        }

        await db.SaveChangesAsync();

        var response = new TeacherResponse(
            teacher.Id, teacher.FirstName, teacher.LastName, teacher.Status,
            instruments.Select(i => i.Id).ToArray(), userId is not null);

        return Results.Created($"/api/teachers/{teacher.Id}", new CreateResponse(response, temporaryPassword));
    }

    private static async Task<IResult> CreateStudentAsync(
        Guid teacherId, CreateStudentRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var teacher = await db.Teachers.SingleOrDefaultAsync(item => item.Id == teacherId)
            ?? throw new NotFoundException("Öğretmen bulunamadı.");
        if (teacher.Status != TeacherStatus.Active)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["teacherId"] = ["Bu öğretmen aktif değil."],
            });

        var instrument = await db.Instruments.SingleOrDefaultAsync(item => item.Id == request.InstrumentId)
            ?? throw new NotFoundException("Enstrüman bulunamadı.");
        var teachesInstrument = await db.TeacherInstruments
            .AnyAsync(item => item.TeacherId == teacherId && item.InstrumentId == request.InstrumentId);
        if (!teachesInstrument)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["instrumentId"] = ["Bu öğretmen bu enstrümanı öğretmiyor."],
            });

        var now = clock.UtcNow;
        var student = Student.Create(request.FirstName, request.LastName, request.BirthDate, now);
        var enrollment = Enrollment.Create(student.Id, teacherId, request.InstrumentId, request.StartedAt, now);
        db.Students.Add(student);
        db.Enrollments.Add(enrollment);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "enrollment.created_with_student",
            nameof(Enrollment),
            enrollment.Id,
            now,
            afterJson: JsonSerializer.Serialize(new
            {
                enrollment.StudentId,
                enrollment.TeacherId,
                enrollment.InstrumentId,
                enrollment.StartedAt,
            })));
        await db.SaveChangesAsync();

        return Results.Created($"/api/students/{student.Id}", new TeacherStudentResponse(
            student.Id, student.FirstName, student.LastName, enrollment.Id,
            instrument.Id, instrument.Name, enrollment.StartedAt));
    }

    private static async Task<IResult> UpdateAsync(Guid teacherId, UpdateRequest request, AbderaDbContext db, IClock clock)
    {
        var teacher = await db.Teachers.SingleOrDefaultAsync(t => t.Id == teacherId)
            ?? throw new NotFoundException("Öğretmen bulunamadı.");

        var instruments = await db.Instruments.Where(i => request.InstrumentIds.Contains(i.Id)).ToListAsync();
        if (instruments.Count != request.InstrumentIds.Distinct().Count())
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["instrumentIds"] = ["Bir veya daha fazla enstrüman bulunamadı."],
            });

        teacher.Update(request.FirstName, request.LastName, clock.UtcNow);
        teacher.SetStatus(request.Status, clock.UtcNow);

        var existingLinks = await db.TeacherInstruments.Where(ti => ti.TeacherId == teacherId).ToListAsync();
        db.TeacherInstruments.RemoveRange(existingLinks);
        foreach (var instrument in instruments)
        {
            db.TeacherInstruments.Add(TeacherInstrument.Create(teacherId, instrument.Id));
        }

        await db.SaveChangesAsync();

        var response = new TeacherResponse(
            teacher.Id, teacher.FirstName, teacher.LastName, teacher.Status,
            instruments.Select(i => i.Id).ToArray(), teacher.UserId is not null);

        return Results.Ok(response);
    }

    private static async Task<List<TeacherResponse>> LoadTeacherResponsesAsync(
        AbderaDbContext db, System.Linq.Expressions.Expression<Func<Teacher, bool>> predicate)
    {
        var teachers = await db.Teachers.Where(predicate)
            .OrderBy(t => t.LastName).ThenBy(t => t.FirstName)
            .ToListAsync();

        var teacherIds = teachers.Select(t => t.Id).ToList();
        var instrumentsByTeacher = await db.TeacherInstruments
            .Where(ti => teacherIds.Contains(ti.TeacherId))
            .ToListAsync();

        return teachers.Select(t => new TeacherResponse(
            t.Id, t.FirstName, t.LastName, t.Status,
            instrumentsByTeacher.Where(ti => ti.TeacherId == t.Id).Select(ti => ti.InstrumentId).ToArray(),
            t.UserId is not null)).ToList();
    }
}
