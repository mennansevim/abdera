using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Pricing.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// Dönem başında aidatları tek tek açmak yerine hepsini bir kerede oluşturur.
// Önce "önizleme" verilir: hangi kayıtlar için aidat açılacak, hangileri zaten var ve
// hangileri ÜCRET PLANI OLMADIĞI için atlanacak. Sonuncusu ekranda "eksikler" olarak
// gösterilir - aidatı unutulan öğrenci sessizce kaybolmasın (docs/15-product-phases.md
// Faz 8: "eksikleri göster").
//
// Kapsam bilinçli olarak dar: yalnızca AYLIK ücret planları. Paket planlar peşin ödenir
// ve dönem kavramı taşımaz (Receivables.ComputeDueDate'e bak), toplu ay üretimine girmez.
public static class BulkReceivables
{
    public record CreateRequest(string Period);

    public record TargetRow(
        Guid EnrollmentId,
        Guid StudentId,
        string StudentName,
        string InstrumentName,
        string TeacherName,
        decimal Amount,
        string Currency);

    public record MissingFeePlanRow(
        Guid EnrollmentId,
        Guid StudentId,
        string StudentName,
        string InstrumentName,
        string TeacherName,
        string Reason);

    public record PlanResponse(
        string Period,
        List<TargetRow> Ready,
        List<TargetRow> AlreadyExists,
        List<MissingFeePlanRow> Missing,
        decimal ReadyTotal,
        string Currency);

    public record CreateResponse(
        string Period,
        int CreatedCount,
        decimal CreatedTotal,
        string Currency,
        int AlreadyExistsCount,
        List<MissingFeePlanRow> Missing);

    public static void MapBulkReceivables(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/receivables").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("/bulk-preview", PreviewAsync);
        group.MapPost("/bulk", CreateAsync);
    }

    private static async Task<IResult> PreviewAsync(string period, AbderaDbContext db)
    {
        return Results.Ok(await BuildPlanAsync(period, db));
    }

    private static async Task<IResult> CreateAsync(
        CreateRequest request,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock)
    {
        var plan = await BuildPlanAsync(request.Period, db);
        if (plan.Ready.Count == 0)
            throw new ConflictException($"'{request.Period}' dönemi için oluşturulacak yeni aidat yok.");

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var enrollmentIds = plan.Ready.Select(row => row.EnrollmentId).ToList();
        var feePlans = await db.FeePlans
            .Where(feePlan => enrollmentIds.Contains(feePlan.EnrollmentId) && feePlan.ActiveUntil == null)
            .ToDictionaryAsync(feePlan => feePlan.EnrollmentId);

        var year = int.Parse(request.Period[..4]);
        var month = int.Parse(request.Period[5..]);
        var created = 0m;

        foreach (var row in plan.Ready)
        {
            var feePlan = feePlans[row.EnrollmentId];
            var dueDate = new DateOnly(year, month, feePlan.DueDay ?? 1);
            var receivable = Receivable.Create(
                row.EnrollmentId, feePlan.Id, feePlan.PriceListItemId, request.Period,
                feePlan.Amount, feePlan.Currency, dueDate, now);

            db.Receivables.Add(receivable);
            created += feePlan.Amount;
            db.AuditLogs.Add(AuditLog.Record(
                actorId,
                "receivable.bulk_created",
                nameof(Receivable),
                receivable.Id,
                now,
                afterJson: JsonSerializer.Serialize(new
                {
                    period = request.Period,
                    amount = feePlan.Amount,
                    currency = feePlan.Currency,
                    enrollmentId = row.EnrollmentId,
                })));
        }

        await db.SaveChangesAsync();

        return Results.Ok(new CreateResponse(
            request.Period,
            plan.Ready.Count,
            created,
            plan.Currency,
            plan.AlreadyExists.Count,
            plan.Missing));
    }

    // Tek sorgu kümesiyle üç listeyi birlikte kurar; önizleme ve oluşturma aynı kuralı
    // paylaşsın diye ortak. Modüller arası okuma StudentBilling.cs'teki desenle aynı:
    // navigation property üzerinden join değil, açık id sorguları.
    private static async Task<PlanResponse> BuildPlanAsync(string period, AbderaDbContext db)
    {
        if (string.IsNullOrWhiteSpace(period) || !Regex.IsMatch(period, @"^\d{4}-(0[1-9]|1[0-2])$"))
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["period"] = ["Dönem 'yyyy-MM' biçiminde olmalı (örn. 2026-09)."],
            });

        var enrollments = await db.Enrollments
            .Where(enrollment => enrollment.Status == EnrollmentStatus.Active)
            .ToListAsync();
        var enrollmentIds = enrollments.Select(enrollment => enrollment.Id).ToList();

        var feePlans = await db.FeePlans
            .Where(feePlan => enrollmentIds.Contains(feePlan.EnrollmentId) && feePlan.ActiveUntil == null)
            .ToDictionaryAsync(feePlan => feePlan.EnrollmentId);
        var existing = await db.Receivables
            .Where(receivable => enrollmentIds.Contains(receivable.EnrollmentId) && receivable.Period == period)
            .Select(receivable => receivable.EnrollmentId)
            .ToListAsync();
        var existingSet = existing.ToHashSet();

        var studentIds = enrollments.Select(enrollment => enrollment.StudentId).Distinct().ToList();
        var teacherIds = enrollments.Select(enrollment => enrollment.TeacherId).Distinct().ToList();
        var instrumentIds = enrollments.Select(enrollment => enrollment.InstrumentId).Distinct().ToList();
        var students = await db.Students.Where(student => studentIds.Contains(student.Id))
            .ToDictionaryAsync(student => student.Id);
        var teachers = await db.Teachers.Where(teacher => teacherIds.Contains(teacher.Id))
            .ToDictionaryAsync(teacher => teacher.Id);
        var instruments = await db.Instruments.Where(instrument => instrumentIds.Contains(instrument.Id))
            .ToDictionaryAsync(instrument => instrument.Id);

        string StudentName(Guid id) => students.TryGetValue(id, out var student) ? $"{student.FirstName} {student.LastName}" : "Öğrenci";
        string TeacherName(Guid id) => teachers.TryGetValue(id, out var teacher) ? $"{teacher.FirstName} {teacher.LastName}" : "Öğretmen";
        string InstrumentName(Guid id) => instruments.TryGetValue(id, out var instrument) ? instrument.Name : "Ders";

        var ready = new List<TargetRow>();
        var alreadyExists = new List<TargetRow>();
        var missing = new List<MissingFeePlanRow>();

        foreach (var enrollment in enrollments)
        {
            var studentName = StudentName(enrollment.StudentId);
            var instrumentName = InstrumentName(enrollment.InstrumentId);
            var teacherName = TeacherName(enrollment.TeacherId);

            if (!feePlans.TryGetValue(enrollment.Id, out var feePlan))
            {
                missing.Add(new MissingFeePlanRow(enrollment.Id, enrollment.StudentId, studentName, instrumentName, teacherName, "Ücret planı yok"));
                continue;
            }

            if (feePlan.BillingType != BillingType.Monthly)
            {
                missing.Add(new MissingFeePlanRow(enrollment.Id, enrollment.StudentId, studentName, instrumentName, teacherName, "Paket planı - toplu ay üretimine girmez"));
                continue;
            }

            var row = new TargetRow(enrollment.Id, enrollment.StudentId, studentName, instrumentName, teacherName, feePlan.Amount, feePlan.Currency);
            if (existingSet.Contains(enrollment.Id)) alreadyExists.Add(row);
            else ready.Add(row);
        }

        var order = (TargetRow row) => row.StudentName;
        return new PlanResponse(
            period,
            ready.OrderBy(order, StringComparer.CurrentCulture).ToList(),
            alreadyExists.OrderBy(order, StringComparer.CurrentCulture).ToList(),
            missing.OrderBy(row => row.StudentName, StringComparer.CurrentCulture).ToList(),
            ready.Sum(row => row.Amount),
            ready.FirstOrDefault()?.Currency ?? alreadyExists.FirstOrDefault()?.Currency ?? "TRY");
    }
}
