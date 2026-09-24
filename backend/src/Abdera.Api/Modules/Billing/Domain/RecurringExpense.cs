using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Billing.Domain;

// Her ay tekrar eden gider kalemi: kira, elektrik/su ortalaması, sabit maaş. Yönetici tutarı
// her ay yeniden girmez; bir kez "aylık şu kadar" der, kalem sona erdirilene kadar her aya
// sayılır (docs/10-decisions.md M9). Tek seferlik giderler (tamir, alet) `Expense`'te kalır.
//
// Tutar değişimi tuition_rates'teki kalıbın aynısı: yeni tutar seçilen AYDAN itibaren geçerli
// olan yeni bir sürüm satırıdır, yürürlükteki sürüm bir önceki ayın son günü kapanır. Geçmiş
// ayların tutarı hiçbir koşulda değişmez, satır silinmez.
public class RecurringExpense
{
    private readonly List<RecurringExpenseAmount> _amounts = [];

    public Guid Id { get; private set; }
    public ExpenseCategory Category { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Note { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<RecurringExpenseAmount> Amounts => _amounts;

    private RecurringExpense() { }

    public static RecurringExpense Create(
        ExpenseCategory category, string name, decimal monthlyAmount, string currency, DateOnly fromMonth,
        string? note, Guid? createdBy, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Gider kalemi adı boş olamaz.", nameof(name));
        var expense = new RecurringExpense
        {
            Id = Guid.NewGuid(),
            Category = category,
            Name = name.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
        };
        expense._amounts.Add(RecurringExpenseAmount.Create(expense.Id, monthlyAmount, currency, FirstOfMonth(fromMonth), createdBy, now));
        return expense;
    }

    // Sayılan (düzeltmeyle geçersizleşmemiş) sürümler, eskiden yeniye.
    public IEnumerable<RecurringExpenseAmount> EffectiveAmounts =>
        _amounts.Where(amount => amount.SupersededAt is null).OrderBy(amount => amount.EffectiveFrom);

    public RecurringExpenseAmount? OpenAmount => EffectiveAmounts.SingleOrDefault(amount => amount.EffectiveUntil is null);

    public bool IsEnded => OpenAmount is null;

    // Verilen aya düşen tutar; kalem o ayda yürürlükte değilse 0.
    public decimal AmountFor(DateOnly month)
    {
        var first = FirstOfMonth(month);
        return EffectiveAmounts.SingleOrDefault(amount => amount.CoversMonth(first))?.MonthlyAmount ?? 0m;
    }

    // `fromMonth`'tan itibaren yeni tutar. Yürürlükteki sürüm bir önceki ayın sonunda kapanır.
    // Aynı ayda ikinci bir değişiklik (yanlış girilen tutarın düzeltmesi) eski sürümü silmez,
    // `SupersededAt` ile geçersiz işaretler - kayıt ve audit izi durur, hesaba artık girmez.
    public RecurringExpenseAmount ChangeAmount(decimal monthlyAmount, string currency, DateOnly fromMonth, Guid? actorId, DateTimeOffset now)
    {
        var from = FirstOfMonth(fromMonth);
        var open = OpenAmount;
        if (open is not null)
        {
            if (from < open.EffectiveFrom)
                throw new ConflictException(
                    $"Yürürlükteki tutar {MonthLabel(open.EffectiveFrom)} ayından beri geçerli. Yeni tutar bu aydan önce başlayamaz - geçmiş ayların tutarı korunur.");
            if (from == open.EffectiveFrom) open.Supersede(now);
            else open.EndOn(from.AddDays(-1), now);
        }
        else
        {
            var last = EffectiveAmounts.Last();
            if (from <= last.EffectiveUntil)
                throw new ConflictException(
                    $"Bu kalem {MonthLabel(last.EffectiveUntil!.Value)} ayında sona erdi. Yeniden başlatmak için sonraki bir ay seç.");
        }

        var amount = RecurringExpenseAmount.Create(Id, monthlyAmount, currency, from, actorId, now);
        _amounts.Add(amount);
        UpdatedAt = now;
        return amount;
    }

    // Kalemi `lastMonth` ayının sonunda bitirir (ör. taşınıldı, kira sözleşmesi bitti).
    public void EndAfter(DateOnly lastMonth, DateTimeOffset now)
    {
        var open = OpenAmount ?? throw new ConflictException("Bu gider kalemi zaten sona erdirilmiş.");
        var first = FirstOfMonth(lastMonth);
        if (first < open.EffectiveFrom)
            throw new ConflictException(
                $"Yürürlükteki tutar {MonthLabel(open.EffectiveFrom)} ayında başlıyor; kalem bu aydan önce sona erdirilemez.");
        open.EndOn(first.AddMonths(1).AddDays(-1), now);
        UpdatedAt = now;
    }

    public static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    // API ayları BillingPeriod biçiminde ("2026-09") taşır - yönetici gün değil ay seçer.
    public static string FormatMonth(DateOnly date) => BillingPeriod.Format(date.Year, date.Month);

    private static string MonthLabel(DateOnly date) => FormatMonth(date);
}

// Bir kalemin belirli bir aydan itibaren geçerli aylık tutarı. `EffectiveFrom` her zaman ayın
// ilk günü, `EffectiveUntil` (doluysa) her zaman ayın son günüdür - bir ay iki sürüme bölünmez.
public class RecurringExpenseAmount
{
    public Guid Id { get; private set; }
    public Guid RecurringExpenseId { get; private set; }
    public decimal MonthlyAmount { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveUntil { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private RecurringExpenseAmount() { }

    internal static RecurringExpenseAmount Create(Guid recurringExpenseId, decimal monthlyAmount, string currency, DateOnly from, Guid? createdBy, DateTimeOffset now)
    {
        if (monthlyAmount <= 0) throw new ArgumentException("Aylık tutar pozitif olmalı.", nameof(monthlyAmount));
        return new RecurringExpenseAmount
        {
            Id = Guid.NewGuid(),
            RecurringExpenseId = recurringExpenseId,
            MonthlyAmount = monthlyAmount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "TRY" : currency.Trim().ToUpperInvariant(),
            EffectiveFrom = from,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public bool CoversMonth(DateOnly firstOfMonth) =>
        EffectiveFrom <= firstOfMonth && (EffectiveUntil is null || firstOfMonth <= EffectiveUntil);

    internal void EndOn(DateOnly lastDay, DateTimeOffset now)
    {
        if (lastDay < EffectiveFrom) throw new ArgumentException("Bitiş başlangıçtan önce olamaz.", nameof(lastDay));
        EffectiveUntil = lastDay;
        UpdatedAt = now;
    }

    internal void Supersede(DateTimeOffset now)
    {
        SupersededAt = now;
        UpdatedAt = now;
    }
}
