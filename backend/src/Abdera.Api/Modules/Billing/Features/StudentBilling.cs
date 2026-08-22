using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// docs/07-api.md GET /api/students/{studentId}/billing - bir öğrencinin tüm kayıtlarındaki
// aidat geçmişi tek ekranda (Admin UX: "payment list" - docs/00-master-prompt.md).
public static class StudentBilling
{
    public record StudentBillingResponse(Guid EnrollmentId, Guid InstrumentId, List<Receivables.ReceivableResponse> Receivables);

    public static void MapStudentBilling(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/students/{studentId:guid}/billing", HandleAsync)
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
}
