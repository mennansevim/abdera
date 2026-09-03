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
    public record StudentSearchResponse(
        Guid StudentId,
        string StudentName,
        Guid TeacherId,
        string TeacherName,
        Guid InstrumentId,
        string InstrumentName,
        string? GuardianPhoneMasked);
    public record StudentInstrumentSummary(Guid InstrumentId, string InstrumentName);
    public record StudentOverviewResponse(StudentResponse Student, List<StudentInstrumentSummary> Instruments);

    public static void MapStudents(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/students").RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);

        group.MapGet("", ListAsync);
        // Öğrenci listesinde "içine girmeden" enstrüman rozetleri gösterebilmek için - N+1
        // yerine (150 öğrenci için 150 ayrı /enrollments isteği) tek bir toplu sorgu.
        group.MapGet("/overview", OverviewAsync);
        group.MapGet("/search", SearchAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("/{studentId:guid}", GetAsync);
        group.MapPost("", CreateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapPatch("/{studentId:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> OverviewAsync(ClaimsPrincipal principal, AbderaDbContext db)
    {
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);

        var studentsQuery = db.Students.AsQueryable();
        if (teacherScope is { } scopedTeacherId)
        {
            var assignedStudentIds = db.Enrollments.Where(e => e.TeacherId == scopedTeacherId).Select(e => e.StudentId);
            studentsQuery = studentsQuery.Where(s => assignedStudentIds.Contains(s.Id));
        }

        var students = await studentsQuery
            .OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
            .Select(s => new StudentResponse(s.Id, s.FirstName, s.LastName, s.BirthDate, s.Status))
            .ToListAsync();

        var studentIds = students.Select(s => s.Id).ToList();
        var enrollmentsQuery = db.Enrollments.Where(e => studentIds.Contains(e.StudentId) && e.Status == EnrollmentStatus.Active);
        // Bir öğrenci birden fazla öğretmenden ders alabilir - Teacher scope'undayken yalnızca
        // KENDİ kursu görünsün, tıpkı /enrollments uç noktasındaki kural gibi (Enrollments.cs
        // ListAsync) - aksi halde bir öğretmen başka bir öğretmenin kurs bilgisini rozet
        // üzerinden görürdü.
        if (teacherScope is { } filterTeacherId)
        {
            enrollmentsQuery = enrollmentsQuery.Where(e => e.TeacherId == filterTeacherId);
        }

        var instrumentsByStudent = await enrollmentsQuery
            .Join(db.Instruments, e => e.InstrumentId, i => i.Id, (e, i) => new { e.StudentId, InstrumentId = i.Id, InstrumentName = i.Name })
            .Distinct()
            .ToListAsync();

        return Results.Ok(students.Select(student => new StudentOverviewResponse(
            student,
            instrumentsByStudent.Where(item => item.StudentId == student.Id)
                .Select(item => new StudentInstrumentSummary(item.InstrumentId, item.InstrumentName))
                .OrderBy(item => item.InstrumentName)
                .ToList())));
    }

    private static async Task<IResult> SearchAsync(string query, AbderaDbContext db)
    {
        var normalized = query.Trim();
        if (normalized.Length < 2)
        {
            return Results.Ok(Array.Empty<StudentSearchResponse>());
        }

        var pattern = $"%{normalized}%";
        var rows = await (
            from student in db.Students
            join enrollment in db.Enrollments on student.Id equals enrollment.StudentId
            join teacher in db.Teachers on enrollment.TeacherId equals teacher.Id
            join instrument in db.Instruments on enrollment.InstrumentId equals instrument.Id
            where student.Status == StudentStatus.Active && enrollment.Status == EnrollmentStatus.Active
            let guardianPhone = (
                from link in db.StudentGuardians
                join guardian in db.Guardians on link.GuardianId equals guardian.Id
                where link.StudentId == student.Id && link.IsPrimary
                select guardian.PhoneNumber).FirstOrDefault()
            where EF.Functions.ILike(student.FirstName + " " + student.LastName, pattern)
                  || EF.Functions.ILike(teacher.FirstName + " " + teacher.LastName, pattern)
                  || EF.Functions.ILike(instrument.Name, pattern)
                  || (guardianPhone != null && EF.Functions.ILike(guardianPhone, pattern))
            orderby student.LastName, student.FirstName, instrument.Name
            select new
            {
                student.Id,
                StudentName = student.FirstName + " " + student.LastName,
                TeacherId = teacher.Id,
                TeacherName = teacher.FirstName + " " + teacher.LastName,
                InstrumentId = instrument.Id,
                InstrumentName = instrument.Name,
                GuardianPhone = guardianPhone,
            })
            .Take(12)
            .ToListAsync();

        return Results.Ok(rows.Select(row => new StudentSearchResponse(
            row.Id,
            row.StudentName,
            row.TeacherId,
            row.TeacherName,
            row.InstrumentId,
            row.InstrumentName,
            MaskPhone(row.GuardianPhone))));
    }

    private static string? MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var visible = phone.Length <= 4 ? phone : phone[^4..];
        return $"••• •• {visible}";
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
