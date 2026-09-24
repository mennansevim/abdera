using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// Her ay kendiliğinden sayılan gider kalemleri (kira, elektrik/su ortalaması, sabit maaş) -
// docs/10-decisions.md M9. Aylar "YYYY-MM" olarak taşınır.
public static class RecurringExpenses
{
    public record CreateRequest(ExpenseCategory Category, string Name, decimal MonthlyAmount, string EffectiveFrom, string? Currency, string? Note);
    public record ChangeAmountRequest(decimal MonthlyAmount, string EffectiveFrom, string? Currency);
    public record EndRequest(string LastMonth);

    public record AmountResponse(Guid Id, decimal MonthlyAmount, string Currency, string EffectiveFrom, string? EffectiveUntil);
    public record RecurringExpenseResponse(
        Guid Id, ExpenseCategory Category, string Name, string? Note, bool IsEnded, decimal CurrentAmount, List<AmountResponse> Amounts);

    public static void MapRecurringExpenses(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recurring-expenses").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPost("/{id:guid}/amounts", ChangeAmountAsync);
        group.MapPost("/{id:guid}/end", EndAsync);
    }

    private static async Task<IResult> ListAsync(AbderaDbContext db, IClock clock)
    {
        var items = await db.RecurringExpenses.AsNoTracking().Include(e => e.Amounts).ToListAsync();
        var thisMonth = CurrentMonth(clock);
        return Results.Ok(items
            .OrderBy(item => item.IsEnded)
            .ThenBy(item => item.Category)
            .ThenBy(item => item.Name)
            .Select(item => ToResponse(item, thisMonth))
            .ToList());
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var from = ParseMonth(request.EffectiveFrom, "effectiveFrom");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 120)
            throw Invalid("name", "Kalem adı zorunlu ve en fazla 120 karakter.");
        if (request.MonthlyAmount <= 0) throw Invalid("monthlyAmount", "Aylık tutar pozitif olmalı.");

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var item = RecurringExpense.Create(request.Category, request.Name, request.MonthlyAmount, request.Currency ?? "TRY", from, request.Note, actorId, now);
        db.RecurringExpenses.Add(item);
        var amount = item.Amounts.Single();
        db.AuditLogs.Add(AuditLog.Record(actorId, "recurring_expense.created", nameof(RecurringExpense), item.Id, now,
            afterJson: JsonSerializer.Serialize(new
            {
                category = item.Category.ToString(),
                name = item.Name,
                monthlyAmount = amount.MonthlyAmount,
                currency = amount.Currency,
                effectiveFrom = RecurringExpense.FormatMonth(amount.EffectiveFrom),
            })));
        await db.SaveChangesAsync();
        return Results.Created($"/api/recurring-expenses/{item.Id}", ToResponse(item, CurrentMonth(clock)));
    }

    private static async Task<IResult> ChangeAmountAsync(Guid id, ChangeAmountRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var from = ParseMonth(request.EffectiveFrom, "effectiveFrom");
        if (request.MonthlyAmount <= 0) throw Invalid("monthlyAmount", "Aylık tutar pozitif olmalı.");
        var item = await LoadAsync(db, id);

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var previous = item.OpenAmount;
        var before = previous is null ? null : JsonSerializer.Serialize(new
        {
            monthlyAmount = previous.MonthlyAmount,
            effectiveFrom = RecurringExpense.FormatMonth(previous.EffectiveFrom),
        });
        var amount = item.ChangeAmount(request.MonthlyAmount, request.Currency ?? previous?.Currency ?? "TRY", from, actorId, now);
        db.AuditLogs.Add(AuditLog.Record(actorId, "recurring_expense.amount_changed", nameof(RecurringExpense), item.Id, now,
            beforeJson: before,
            afterJson: JsonSerializer.Serialize(new
            {
                monthlyAmount = amount.MonthlyAmount,
                currency = amount.Currency,
                effectiveFrom = RecurringExpense.FormatMonth(amount.EffectiveFrom),
                // Aynı ayda düzeltme: önceki sürüm silinmedi, geçersiz işaretlendi.
                correctedSameMonth = previous?.SupersededAt is not null,
            })));
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(item, CurrentMonth(clock)));
    }

    private static async Task<IResult> EndAsync(Guid id, EndRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var lastMonth = ParseMonth(request.LastMonth, "lastMonth");
        var item = await LoadAsync(db, id);
        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        item.EndAfter(lastMonth, now);
        db.AuditLogs.Add(AuditLog.Record(actorId, "recurring_expense.ended", nameof(RecurringExpense), item.Id, now,
            afterJson: JsonSerializer.Serialize(new { lastMonth = RecurringExpense.FormatMonth(lastMonth) })));
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(item, CurrentMonth(clock)));
    }

    private static async Task<RecurringExpense> LoadAsync(AbderaDbContext db, Guid id) =>
        await db.RecurringExpenses.Include(e => e.Amounts).SingleOrDefaultAsync(e => e.Id == id)
        ?? throw new NotFoundException("Gider kalemi bulunamadı.");

    private static DateOnly CurrentMonth(IClock clock) =>
        RecurringExpense.FirstOfMonth(DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date));

    private static DateOnly ParseMonth(string? value, string field)
    {
        var (year, month) = BillingPeriod.Parse(value?.Trim(), field);
        return new DateOnly(year, month, 1);
    }

    private static ValidationFailedException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static RecurringExpenseResponse ToResponse(RecurringExpense item, DateOnly thisMonth) => new(
        item.Id, item.Category, item.Name, item.Note, item.IsEnded, item.AmountFor(thisMonth),
        item.EffectiveAmounts.Select(amount => new AmountResponse(
            amount.Id, amount.MonthlyAmount, amount.Currency,
            RecurringExpense.FormatMonth(amount.EffectiveFrom),
            amount.EffectiveUntil is { } until ? RecurringExpense.FormatMonth(until) : null)).ToList());
}
