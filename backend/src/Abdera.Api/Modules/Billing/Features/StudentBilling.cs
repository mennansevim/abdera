using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// docs/07-api.md GET /api/students/{studentId}/billing - bir öğrencinin tüm kayıtlarındaki
// aidat geçmişi tek ekranda (Admin UX: "payment list" - docs/00-master-prompt.md).
public static class StudentBilling
{
    public record StudentBillingResponse(Guid EnrollmentId, Guid InstrumentId, List<Receivables.ReceivableResponse> Receivables);
    public record DueListItemResponse(
        Guid Id, Guid EnrollmentId, Guid StudentId, string StudentName, Guid TeacherId, string TeacherName, Guid InstrumentId, string InstrumentName,
        string Period, decimal Amount, string Currency, DateOnly DueDate, ReceivableStatus Status,
        decimal TotalPaid, List<Receivables.PaymentSummary> Payments);

    public static void MapStudentBilling(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/students/{studentId:guid}/billing", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
        app.MapGet("/api/billing/dues", ListDuesAsync)
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> HandleAsync(Guid studentId, AbderaDbContext db)
    {
        var enrollments = await db.Enrollments.Where(e => e.StudentId == studentId).ToListAsync();
        var enrollmentIds = enrollments.Select(e => e.Id).ToList();

        var receivables = await db.Receivables.Where(r => enrollmentIds.Contains(r.EnrollmentId))
            .OrderByDescending(r => r.DueDate)
            .ToListAsync();

        var totals = await Receivables.ComputeTotalsPaidAsync(receivables.Select(r => r.Id), db);
        var payments = await Receivables.ComputePaymentsAsync(receivables.Select(r => r.Id), db);

        var result = enrollments.Select(e => new StudentBillingResponse(
            e.Id, e.InstrumentId,
                receivables.Where(r => r.EnrollmentId == e.Id)
                .Select(r => Receivables.ToResponse(r, totals.GetValueOrDefault(r.Id), payments.GetValueOrDefault(r.Id) ?? []))
                .ToList()));

        return Results.Ok(result);
    }

    private static async Task<IResult> ListDuesAsync(AbderaDbContext db)
    {
        var receivables = await db.Receivables.OrderByDescending(receivable => receivable.DueDate).ToListAsync();
        var enrollmentIds = receivables.Select(receivable => receivable.EnrollmentId).Distinct().ToList();
        var enrollments = await db.Enrollments.Where(enrollment => enrollmentIds.Contains(enrollment.Id)).ToDictionaryAsync(enrollment => enrollment.Id);
        var studentIds = enrollments.Values.Select(enrollment => enrollment.StudentId).Distinct().ToList();
        var instrumentIds = enrollments.Values.Select(enrollment => enrollment.InstrumentId).Distinct().ToList();
        var students = await db.Students.Where(student => studentIds.Contains(student.Id)).ToDictionaryAsync(student => student.Id);
        var teacherIds = enrollments.Values.Select(enrollment => enrollment.TeacherId).Distinct().ToList();
        var teachers = await db.Teachers.Where(teacher => teacherIds.Contains(teacher.Id)).ToDictionaryAsync(teacher => teacher.Id);
        var instruments = await db.Instruments.Where(instrument => instrumentIds.Contains(instrument.Id)).ToDictionaryAsync(instrument => instrument.Id);
        var totals = await Receivables.ComputeTotalsPaidAsync(receivables.Select(receivable => receivable.Id), db);
        var payments = await Receivables.ComputePaymentsAsync(receivables.Select(receivable => receivable.Id), db);

        var result = receivables.Select(receivable =>
        {
            var enrollment = enrollments[receivable.EnrollmentId];
            var student = students[enrollment.StudentId];
            var teacher = teachers[enrollment.TeacherId];
            var instrument = instruments[enrollment.InstrumentId];
            return new DueListItemResponse(
                receivable.Id, receivable.EnrollmentId, student.Id, $"{student.FirstName} {student.LastName}", teacher.Id, $"{teacher.FirstName} {teacher.LastName}",
                instrument.Id, instrument.Name, receivable.Period, receivable.Amount, receivable.Currency,
                receivable.DueDate, receivable.Status, totals.GetValueOrDefault(receivable.Id),
                payments.GetValueOrDefault(receivable.Id) ?? []);
        });

        return Results.Ok(result);
    }
}
