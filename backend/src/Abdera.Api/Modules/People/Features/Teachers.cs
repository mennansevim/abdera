using Abdera.Api.Modules.Auth.Domain;
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

    public static void MapTeachers(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/teachers").RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapPatch("/{teacherId:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db)
    {
        var teachers = await LoadTeacherResponsesAsync(db, t => true);
        return Results.Ok(teachers);
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
