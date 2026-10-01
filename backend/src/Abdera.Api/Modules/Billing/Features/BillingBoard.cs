using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Infrastructure;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// Aidatlar ekranının (docs/10-decisions.md H17) tek veri kaynağı: bir yılın her kurs kaydı ×
// her ayı. Aynı yanıttan iki görünüm çizilir - Liste (seçili ayın bekleyenleri) ve Çizelge
// (öğrenci × ay). Kayıt başına bir satır: iki kursa giden öğrenci iki satırda görünür, çünkü
// aidat kurs kaydı başına açılır ve tahsilat da o satıra yazılır.
public static class BillingBoard
{
    public record Cell(
        string Period, Guid ReceivableId, ReceivableStatus Status, decimal Amount, decimal TotalPaid,
        decimal BaseAmount, decimal DiscountPercent, string? DiscountReason, string Currency, DateOnly? LastPaymentDate);

    public record BoardRow(
        Guid EnrollmentId, Guid StudentId, string StudentName, Guid TeacherId, string TeacherName,
        string InstrumentName, CourseKind CourseKind, string GuardianNames,
        bool SiblingDiscount, bool MultiCourse, decimal? ManualDiscountPercent, string? ManualDiscountReason,
        DateOnly StartedAt, DateOnly? EndedAt, EnrollmentStatus EnrollmentStatus,
        List<Cell> Cells);

    public static void MapBillingBoard(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/billing/board", HandleAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> HandleAsync(int? year, AbderaDbContext db, IClock clock, IConfiguration configuration)
    {
        // StudentBilling.ListDuesAsync ile aynı yedek güvence: serverless yayında bu ayın
        // eksik aidatları ekran açılırken açılır.
        if (configuration.GetValue("Runtime:Serverless", false))
            await BillingDailyJob.OpenCurrentPeriodAsync(db, clock);

        var today = clock.ToSchoolLocal(clock.UtcNow);
        var y = year ?? today.Year;
        if (y is < 2000 or > 2100)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["year"] = ["Geçersiz yıl."] });

        var yearStart = new DateOnly(y, 1, 1);
        var yearEnd = new DateOnly(y, 12, 31);
        var enrollments = await db.Enrollments
            .Where(e => e.StartedAt <= yearEnd && (e.EndedAt == null || e.EndedAt >= yearStart))
            .ToListAsync();

        var enrollmentIds = enrollments.Select(e => e.Id).ToList();
        var studentIds = enrollments.Select(e => e.StudentId).Distinct().ToList();
        var teacherIds = enrollments.Select(e => e.TeacherId).Distinct().ToList();
        var instrumentIds = enrollments.Select(e => e.InstrumentId).Distinct().ToList();

        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        var instruments = await db.Instruments.AsNoTracking().Where(i => instrumentIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id);

        // Veli adı yalnızca aramada kullanılır ("veliyi yazınca çocuğu bulayım").
        var guardianLinks = await db.StudentGuardians.AsNoTracking()
            .Where(link => studentIds.Contains(link.StudentId))
            .Join(db.Guardians, link => link.GuardianId, guardian => guardian.Id,
                (link, guardian) => new { link.StudentId, guardian.FirstName, guardian.LastName })
            .ToListAsync();
        var guardianNames = guardianLinks
            .GroupBy(link => link.StudentId)
            .ToDictionary(group => group.Key, group => string.Join(", ", group.Select(link => $"{link.FirstName} {link.LastName}")));

        var prefix = $"{y:D4}-";
        var receivables = await db.Receivables.AsNoTracking()
            .Where(r => enrollmentIds.Contains(r.EnrollmentId) && r.Period.StartsWith(prefix))
            .ToListAsync();
        var receivableIds = receivables.Select(r => r.Id).ToList();
        var totals = await Receivables.ComputeTotalsPaidAsync(receivableIds, db);
        var lastPayments = await db.Payments.AsNoTracking()
            .Where(p => receivableIds.Contains(p.ReceivableId))
            .GroupBy(p => p.ReceivableId)
            .Select(g => new { ReceivableId = g.Key, Last = g.Max(p => p.PaymentDate) })
            .ToDictionaryAsync(x => x.ReceivableId, x => x.Last);

        var pricer = await TuitionPricer.LoadAsync(db);

        var rows = enrollments
            .Where(e => students.ContainsKey(e.StudentId) && teachers.ContainsKey(e.TeacherId) && instruments.ContainsKey(e.InstrumentId))
            .Select(e =>
            {
                var student = students[e.StudentId];
                var teacher = teachers[e.TeacherId];
                var facts = pricer.ContextFor(e);
                var cells = receivables
                    .Where(r => r.EnrollmentId == e.Id)
                    .OrderBy(r => r.Period)
                    .Select(r => new Cell(
                        r.Period, r.Id, r.Status, r.Amount, totals.GetValueOrDefault(r.Id), r.BaseAmount,
                        r.DiscountPercent, r.DiscountReason, r.Currency,
                        lastPayments.TryGetValue(r.Id, out var last) ? last : null))
                    .ToList();
                return new BoardRow(
                    e.Id, student.Id, $"{student.FirstName} {student.LastName}", teacher.Id, $"{teacher.FirstName} {teacher.LastName}",
                    instruments[e.InstrumentId].Name, e.CourseKind, guardianNames.GetValueOrDefault(student.Id, ""),
                    facts.HasSibling, facts.AttendsMultipleCourses, e.ManualDiscountPercent, e.ManualDiscountReason,
                    e.StartedAt, e.EndedAt, e.Status, cells);
            })
            // Türkçe alfabetik sıra istemcide (localeCompare "tr") - burada named culture
            // kullanmak Alpine imajında ICU'ya bağımlılık demek (CLAUDE.md).
            .ToList();

        return Results.Ok(rows);
    }
}
