using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

public static class Expenses
{
    public record CreateRequest(ExpenseCategory Category, string Description, decimal Amount, string? Currency, DateOnly ExpenseDate, string? Note);
    public record ExpenseResponse(Guid Id, ExpenseCategory Category, string Description, decimal Amount, string Currency, DateOnly ExpenseDate, string? Note);

    public static void MapExpenses(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/expenses").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
    }

    private static async Task<IResult> ListAsync(DateOnly? from, DateOnly? to, AbderaDbContext db)
    {
        var query = db.Expenses.AsQueryable();
        if (from is { } start) query = query.Where(e => e.ExpenseDate >= start);
        if (to is { } end) query = query.Where(e => e.ExpenseDate <= end);
        var result = await query.OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.CreatedAt).Select(ToResponseExpression()).ToListAsync();
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Description))
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["expense"] = ["Açıklama ve pozitif tutar zorunludur."] });

        var actorId = AuthContext.GetUserId(principal);
        var expense = Expense.Create(request.Category, request.Description, request.Amount, request.Currency ?? "TRY", request.ExpenseDate, request.Note, actorId, clock.UtcNow);
        db.Expenses.Add(expense);
        db.AuditLogs.Add(AuditLog.Record(actorId, "expense.created", nameof(Expense), expense.Id, clock.UtcNow,
            afterJson: JsonSerializer.Serialize(new { category = expense.Category.ToString(), expense.Amount, expense.ExpenseDate })));
        await db.SaveChangesAsync();
        return Results.Created($"/api/expenses/{expense.Id}", ToResponse(expense));
    }

    private static ExpenseResponse ToResponse(Expense expense) => new(expense.Id, expense.Category, expense.Description, expense.Amount, expense.Currency, expense.ExpenseDate, expense.Note);
    private static System.Linq.Expressions.Expression<Func<Expense, ExpenseResponse>> ToResponseExpression() => e => new(e.Id, e.Category, e.Description, e.Amount, e.Currency, e.ExpenseDate, e.Note);
}
