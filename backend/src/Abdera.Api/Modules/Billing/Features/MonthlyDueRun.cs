using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Infrastructure;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// Ay başında tek düğme: "Eylül aidatlarını oluştur". Ön koşulu yalnızca ücret tarifesinin
// tanımlı olması - eskiden her kurs kaydı için ayrıca bir "ücret planı" açılmış olması
// gerekiyordu ve planı olmayan öğrenci sessizce atlanıyordu.
//
// Önizleme ve oluşturma AYNI fonksiyonu (BuildPlanAsync) paylaşır: ekranda görülen tutar
// ile yazılan tutarın ayrışması parada en tehlikeli hata sınıfı.
public static class MonthlyDueRun
{
    public record CreateRequest(string Period);

    public record TargetRow(
        Guid EnrollmentId,
        Guid StudentId,
        string StudentName,
        string InstrumentName,
        string TeacherName,
        CourseKind CourseKind,
        decimal BaseAmount,
        decimal DiscountPercent,
        string? DiscountReason,
        decimal Amount,
        string Currency);

    public record MissingRow(
        Guid EnrollmentId,
        Guid StudentId,
        string StudentName,
        string InstrumentName,
        string TeacherName,
        CourseKind CourseKind,
        string Reason);

    public record PlanResponse(
        string Period,
        DateOnly DueDate,
        List<TargetRow> Ready,
        List<TargetRow> AlreadyExists,
        List<MissingRow> Missing,
        decimal ReadyBaseTotal,
        decimal ReadyTotal,
        decimal ReadyDiscountTotal,
        string Currency);

    public record CreateResponse(
        string Period,
        int CreatedCount,
        decimal CreatedTotal,
        decimal CreatedDiscountTotal,
        string Currency,
        int AlreadyExistsCount,
        List<MissingRow> Missing);

    public static void MapMonthlyDueRun(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/receivables").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("/monthly-run", PreviewAsync);
        group.MapPost("/monthly-run", CreateAsync);
    }

    private static async Task<IResult> PreviewAsync(string period, AbderaDbContext db) =>
        Results.Ok(await BuildPlanAsync(period, db));

    private static async Task<IResult> CreateAsync(
        CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var plan = await BuildPlanAsync(request.Period, db);
        if (plan.Ready.Count == 0)
            throw new ConflictException($"'{request.Period}' dönemi için oluşturulacak yeni aidat yok.");

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var pricer = await TuitionPricer.LoadAsync(db);
        var enrollmentIds = plan.Ready.Select(row => row.EnrollmentId).ToList();
        var enrollments = await db.Enrollments
            .Where(enrollment => enrollmentIds.Contains(enrollment.Id))
            .ToDictionaryAsync(enrollment => enrollment.Id);

        var createdTotal = 0m;
        var discountTotal = 0m;

        foreach (var row in plan.Ready)
        {
            var enrollment = enrollments[row.EnrollmentId];
            // Önizlemedeki satırı tekrar fiyatlamak yerine aynı hesabı yeniden çalıştırmak
            // bilinçli: araya giren bir tarife/politika değişikliği burada yakalanır ve
            // yazılan tutar her zaman o anki kuralın sonucudur.
            var priced = pricer.Price(enrollment, request.Period);
            if (priced is null) continue;

            var receivable = Receivable.Create(
                enrollment.Id, priced.Value.Rate.Id, request.Period, priced.Value.Breakdown,
                priced.Value.Rate.Currency, pricer.DueDateFor(request.Period), now);

            db.Receivables.Add(receivable);
            createdTotal += receivable.Amount;
            discountTotal += receivable.BaseAmount - receivable.Amount;

            db.AuditLogs.Add(AuditLog.Record(
                actorId, "receivable.monthly_run_created", nameof(Receivable), receivable.Id, now,
                afterJson: JsonSerializer.Serialize(new
                {
                    period = request.Period,
                    baseAmount = receivable.BaseAmount,
                    discountPercent = receivable.DiscountPercent,
                    discountReason = receivable.DiscountReason,
                    amount = receivable.Amount,
                    currency = receivable.Currency,
                    enrollmentId = receivable.EnrollmentId,
                })));
        }

        await db.SaveChangesAsync();

        return Results.Ok(new CreateResponse(
            request.Period, plan.Ready.Count, createdTotal, discountTotal,
            plan.Currency, plan.AlreadyExists.Count, plan.Missing));
    }

    // Modüller arası okuma navigation property üzerinden join değil, açık id sorguları
    // (CLAUDE.md modül sınırı kuralı).
    private static async Task<PlanResponse> BuildPlanAsync(string period, AbderaDbContext db)
    {
        BillingPeriod.Parse(period);

        var pricer = await TuitionPricer.LoadAsync(db);
        var enrollments = await db.Enrollments
            .Where(enrollment => enrollment.Status == EnrollmentStatus.Active)
            .ToListAsync();
        var enrollmentIds = enrollments.Select(enrollment => enrollment.Id).ToList();

        var existing = (await db.Receivables
            .Where(receivable => enrollmentIds.Contains(receivable.EnrollmentId) && receivable.Period == period)
            .Select(receivable => receivable.EnrollmentId)
            .ToListAsync()).ToHashSet();

        var studentIds = enrollments.Select(enrollment => enrollment.StudentId).Distinct().ToList();
        var teacherIds = enrollments.Select(enrollment => enrollment.TeacherId).Distinct().ToList();
        var instrumentIds = enrollments.Select(enrollment => enrollment.InstrumentId).Distinct().ToList();
        var students = await db.Students.Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var teachers = await db.Teachers.Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        var instruments = await db.Instruments.Where(i => instrumentIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

        string StudentName(Guid id) => students.TryGetValue(id, out var s) ? $"{s.FirstName} {s.LastName}" : "Öğrenci";
        string TeacherName(Guid id) => teachers.TryGetValue(id, out var t) ? $"{t.FirstName} {t.LastName}" : "Öğretmen";
        string InstrumentName(Guid id) => instruments.TryGetValue(id, out var i) ? i.Name : "Ders";

        var ready = new List<TargetRow>();
        var alreadyExists = new List<TargetRow>();
        var missing = new List<MissingRow>();

        foreach (var enrollment in enrollments)
        {
            var studentName = StudentName(enrollment.StudentId);
            var instrumentName = InstrumentName(enrollment.InstrumentId);
            var teacherName = TeacherName(enrollment.TeacherId);

            var priced = pricer.Price(enrollment, period);
            if (priced is null)
            {
                var kindLabel = enrollment.CourseKind == CourseKind.Group ? "Grup" : "Birebir";
                missing.Add(new MissingRow(
                    enrollment.Id, enrollment.StudentId, studentName, instrumentName, teacherName,
                    enrollment.CourseKind, $"{kindLabel} dersi için bu dönemde geçerli ücret tarifesi yok"));
                continue;
            }

            var breakdown = priced.Value.Breakdown;
            var row = new TargetRow(
                enrollment.Id, enrollment.StudentId, studentName, instrumentName, teacherName,
                enrollment.CourseKind, breakdown.BaseAmount, breakdown.DiscountPercent,
                breakdown.DiscountReason, breakdown.NetAmount, priced.Value.Rate.Currency);

            if (existing.Contains(enrollment.Id)) alreadyExists.Add(row);
            else ready.Add(row);
        }

        var byName = (TargetRow row) => row.StudentName;
        return new PlanResponse(
            period,
            pricer.DueDateFor(period),
            ready.OrderBy(byName, StringComparer.CurrentCulture).ToList(),
            alreadyExists.OrderBy(byName, StringComparer.CurrentCulture).ToList(),
            missing.OrderBy(row => row.StudentName, StringComparer.CurrentCulture).ToList(),
            ready.Sum(row => row.BaseAmount),
            ready.Sum(row => row.Amount),
            ready.Sum(row => row.BaseAmount - row.Amount),
            ready.FirstOrDefault()?.Currency ?? alreadyExists.FirstOrDefault()?.Currency ?? "TRY");
    }
}
