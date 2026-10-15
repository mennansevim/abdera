using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Abdera.Api.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// Grup dersi öğretmenlerine aylık ödeme (docs/10-decisions.md AC): dönemin tahakkuk eden grup
// aidatlarının %40'ı. Haftalık ödemenin (TeacherPayouts) aynı kalıbı: ayın TABLOSU (öğretmen,
// grup geliri, pay, ödendi mi) ve o ayın ödemesini Maaş gideri olarak yazmak. Tutarı sunucu
// hesaplar (GroupLessonPay), istemci yalnızca gördüğünü teyit eder.
public static class TeacherGroupPayouts
{
    public record CreateRequest(
        Guid TeacherId,
        string Period,
        decimal ExpectedRevenueBase,
        decimal ExpectedAmount,
        decimal? AgreedAmount,
        DateOnly? PaidOn,
        string? Note);

    public record PayoutResponse(
        Guid Id, Guid TeacherId, string Period, decimal RevenueBase, int ReceivableCount, decimal SharePercent,
        decimal ComputedAmount, decimal Amount, string Currency, DateOnly PaidOn, string? Note, Guid ExpenseId,
        DateTimeOffset CreatedAt);

    public record TeacherMonthRow(
        Guid TeacherId, string FirstName, string LastName, TeacherStatus Status,
        decimal RevenueBase, int ReceivableCount, decimal SharePercent, decimal ComputedAmount, string Currency,
        PayoutResponse? Payout);

    public record MonthResponse(
        string Period, bool IsCurrentMonth, decimal SharePercent, decimal PaidTotal, decimal PayableTotal, string Currency,
        List<TeacherMonthRow> Teachers);

    public static void MapTeacherGroupPayouts(this IEndpointRouteBuilder app)
    {
        // Maaş verisi yalnızca Admin'e açık (TeacherPayouts ile aynı kural).
        var group = app.MapGroup("/api/teacher-payouts/group-month").RequireAuthorization(AuthorizationPolicies.AdminOnly)
            .RequireModule(ModuleKeys.Finance).RequireModule(ModuleKeys.Groups);
        group.MapGet("", MonthAsync);
        group.MapPost("", CreateAsync);
    }

    private static async Task<IResult> MonthAsync(string? period, AbderaDbContext db, IClock clock)
    {
        var todayLocal = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var currentPeriod = BillingPeriod.Format(todayLocal.Year, todayLocal.Month);
        var resolved = period ?? currentPeriod;
        BillingPeriod.Parse(resolved);

        var revenue = await GroupLessonPay.RevenueByTeacherAsync(db, [resolved]);
        var payouts = await db.TeacherMonthlyGroupPayouts.AsNoTracking().Where(p => p.Period == resolved).ToListAsync();
        // Grup geliri ya da ödemesi olan öğretmenler; ayrıca aktif bir grubu olan (henüz aidatı
        // açılmamış) öğretmen de listede görünsün ki "neden 0" sorusu ekrandan yanıtlansın.
        var groupTeacherIds = await db.LessonGroups.AsNoTracking()
            .Where(g => g.Status == Scheduling.Domain.LessonGroupStatus.Active)
            .Select(g => g.TeacherId).Distinct().ToListAsync();
        var teacherIds = revenue.Select(r => r.TeacherId).Concat(payouts.Select(p => p.TeacherId)).Concat(groupTeacherIds).ToHashSet();
        var teachers = await db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id)).ToListAsync();

        var rows = teachers
            .Select(teacher =>
            {
                var income = revenue.SingleOrDefault(r => r.TeacherId == teacher.Id);
                var payout = payouts.SingleOrDefault(p => p.TeacherId == teacher.Id);
                var revenueBase = income?.RevenueBase ?? 0m;
                return new TeacherMonthRow(
                    teacher.Id, teacher.FirstName, teacher.LastName, teacher.Status,
                    revenueBase, income?.ReceivableCount ?? 0, GroupLessonPay.SharePercent,
                    GroupLessonPay.ComputeAmount(revenueBase), payout?.Currency ?? income?.Currency ?? "TRY",
                    payout is null ? null : ToResponse(payout));
            })
            .OrderBy(row => $"{row.FirstName} {row.LastName}", StringComparer.Create(TurkishCulture, ignoreCase: true))
            .ToList();

        return Results.Ok(new MonthResponse(
            resolved, resolved == currentPeriod, GroupLessonPay.SharePercent,
            rows.Sum(row => row.Payout?.Amount ?? 0m),
            rows.Where(row => row.Payout is null).Sum(row => row.ComputedAmount),
            rows.Select(row => row.Currency).FirstOrDefault() ?? "TRY",
            rows));
    }

    private static async Task<IResult> CreateAsync(
        CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var (year, month) = BillingPeriod.Parse(request.Period);
        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);
        var todayLocal = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        if (firstDay > todayLocal) throw Invalid("period", "Henüz başlamamış bir ayın ödemesi yapılamaz.");

        var teacher = await db.Teachers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.TeacherId)
            ?? throw new NotFoundException("Öğretmen bulunamadı.");

        if (await AlreadyPaidAsync(db, teacher.Id, request.Period))
            throw new ConflictException($"{teacher.FirstName} {teacher.LastName} için bu ayın grup dersi ödemesi zaten yapılmış.");

        var income = (await GroupLessonPay.RevenueByTeacherAsync(db, [request.Period], teacher.Id)).SingleOrDefault();
        if (income is null || income.RevenueBase <= 0)
            throw Invalid("period", "Bu ay bu öğretmenin grup dersi geliri yok; ödenecek bir tutar çıkmıyor.");

        var computedAmount = GroupLessonPay.ComputeAmount(income.RevenueBase);
        if (request.ExpectedRevenueBase != income.RevenueBase || request.ExpectedAmount != computedAmount)
            throw new ConflictException(
                $"Bu ay grup geliri {Money(income.RevenueBase, income.Currency)}, pay {Money(computedAmount, income.Currency)} görünüyor " +
                $"(ekranda {Money(request.ExpectedRevenueBase, income.Currency)} / {Money(request.ExpectedAmount, income.Currency)} vardı). " +
                "Listeyi yenileyip tekrar dene.");

        var amount = request.AgreedAmount ?? computedAmount;
        if (amount <= 0) throw Invalid("agreedAmount", "Ödeme tutarı pozitif olmalı.");

        var paidOn = request.PaidOn ?? (lastDay < todayLocal ? lastDay : todayLocal);
        if (paidOn < firstDay) throw Invalid("paidOn", "Ödeme tarihi ayın başından önce olamaz.");

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var description = $"Aylık grup dersi payı — {teacher.FirstName} {teacher.LastName} ({firstDay.ToString("MMMM yyyy", TurkishCulture)})";
        var expense = Expense.Create(ExpenseCategory.Salary, description, amount, income.Currency, paidOn, request.Note, actorId, now);
        var payout = TeacherMonthlyGroupPayout.Create(
            teacher.Id, request.Period, income.RevenueBase, income.ReceivableCount, GroupLessonPay.SharePercent,
            computedAmount, amount, income.Currency, paidOn, request.Note, expense.Id, actorId, now);

        db.Expenses.Add(expense);
        db.TeacherMonthlyGroupPayouts.Add(payout);
        db.AuditLogs.Add(AuditLog.Record(
            actorId, "teacher_group_payout.created", nameof(TeacherMonthlyGroupPayout), payout.Id, now,
            afterJson: JsonSerializer.Serialize(new
            {
                teacherId = payout.TeacherId,
                period = payout.Period,
                revenueBase = payout.RevenueBase,
                receivableCount = payout.ReceivableCount,
                sharePercent = payout.SharePercent,
                computedAmount = payout.ComputedAmount,
                agreedAmount = request.AgreedAmount,
                amount = payout.Amount,
                currency = payout.Currency,
                paidOn = payout.PaidOn,
                expenseId = payout.ExpenseId,
            })));

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await AlreadyPaidAsync(db, teacher.Id, request.Period))
                throw new ConflictException($"{teacher.FirstName} {teacher.LastName} için bu ayın grup dersi ödemesi zaten yapılmış.");
            throw;
        }

        return Results.Created($"/api/teacher-payouts/group-month/{payout.Id}", ToResponse(payout));
    }

    private static Task<bool> AlreadyPaidAsync(AbderaDbContext db, Guid teacherId, string period) =>
        db.TeacherMonthlyGroupPayouts.AsNoTracking().AnyAsync(p => p.TeacherId == teacherId && p.Period == period);

    // Kullanıcıya görünen metin: tr-TR açıkça (CLAUDE.md, Dockerfile ICU notu).
    private static CultureInfo TurkishCulture => CultureInfo.GetCultureInfo("tr-TR");

    private static string Money(decimal amount, string currency) =>
        $"{amount.ToString("N2", TurkishCulture)} {currency}";

    private static ValidationFailedException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static PayoutResponse ToResponse(TeacherMonthlyGroupPayout p) => new(
        p.Id, p.TeacherId, p.Period, p.RevenueBase, p.ReceivableCount, p.SharePercent, p.ComputedAmount,
        p.Amount, p.Currency, p.PaidOn, p.Note, p.ExpenseId, p.CreatedAt);
}
