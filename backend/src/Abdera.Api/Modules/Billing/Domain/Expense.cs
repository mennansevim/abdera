namespace Abdera.Api.Modules.Billing.Domain;

public enum ExpenseCategory
{
    Salary,
    Utilities,
    Rent,
    Other,
}

// Gider kayıtları silinmez; düzeltme gerekiyorsa yeni bir karşı kayıt/audit olayı eklenir.
public class Expense
{
    public Guid Id { get; private set; }
    public ExpenseCategory Category { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public DateOnly ExpenseDate { get; private set; }
    public string? Note { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Expense() { }

    public static Expense Create(ExpenseCategory category, string description, decimal amount, string currency, DateOnly expenseDate, string? note, Guid? createdBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Gider açıklaması boş olamaz.", nameof(description));
        if (amount <= 0) throw new ArgumentException("Gider tutarı pozitif olmalı.", nameof(amount));
        return new Expense
        {
            Id = Guid.NewGuid(),
            Category = category,
            Description = description.Trim(),
            Amount = amount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "TRY" : currency.Trim().ToUpperInvariant(),
            ExpenseDate = expenseDate,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }
}
