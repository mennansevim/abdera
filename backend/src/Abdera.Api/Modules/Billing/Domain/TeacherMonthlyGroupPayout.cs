namespace Abdera.Api.Modules.Billing.Domain;

// Bir öğretmene bir ay için yapılan grup dersi ödemesi (docs/10-decisions.md AC). Haftalık
// ödemenin (TeacherWeeklyPayout) aylık kardeşi: hesabın TAMAMI satıra donar - dönemin grup
// geliri (`RevenueBase`), pay yüzdesi (`SharePercent`), çarpımı (`ComputedAmount`) ve gerçekte
// ödenen (`Amount`). Sonradan açılan/iptal edilen bir aidat ödenmiş ayı değiştirmez.
// Satır silinmez; ödeme aynı transaction'da expenses'a Maaş olarak yazılır (`ExpenseId`).
public class TeacherMonthlyGroupPayout
{
    public Guid Id { get; private set; }
    public Guid TeacherId { get; private set; }
    public string Period { get; private set; } = null!;
    public decimal RevenueBase { get; private set; }
    public int ReceivableCount { get; private set; }
    public decimal SharePercent { get; private set; }
    public decimal ComputedAmount { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public DateOnly PaidOn { get; private set; }
    public string? Note { get; private set; }
    public Guid ExpenseId { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private TeacherMonthlyGroupPayout() { }

    public static TeacherMonthlyGroupPayout Create(
        Guid teacherId, string period, decimal revenueBase, int receivableCount, decimal sharePercent,
        decimal computedAmount, decimal amount, string currency, DateOnly paidOn, string? note,
        Guid expenseId, Guid? createdBy, DateTimeOffset now)
    {
        if (!BillingPeriod.IsValid(period)) throw new ArgumentException("Dönem 'yyyy-MM' biçiminde olmalı.", nameof(period));
        if (revenueBase <= 0) throw new ArgumentOutOfRangeException(nameof(revenueBase), "Bu ay grup geliri yok; ödenecek bir tutar çıkmıyor.");
        if (sharePercent is <= 0 or > 100) throw new ArgumentOutOfRangeException(nameof(sharePercent), "Pay yüzdesi 0 ile 100 arasında olmalı.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Ödeme tutarı pozitif olmalı.");

        return new TeacherMonthlyGroupPayout
        {
            Id = Guid.NewGuid(),
            TeacherId = teacherId,
            Period = period,
            RevenueBase = revenueBase,
            ReceivableCount = receivableCount,
            SharePercent = sharePercent,
            ComputedAmount = computedAmount,
            Amount = amount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "TRY" : currency.Trim().ToUpperInvariant(),
            PaidOn = paidOn,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ExpenseId = expenseId,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }
}
