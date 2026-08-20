using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Billing.Domain;

// docs/03-erd.md - Billing > receivables. docs/05-state-models.md: OVERDUE'ya giden tek yol
// gecelik sweeper'dır (Unpaid/Partial + vade geçmiş) - ödeme kaydı asla doğrudan Overdue
// üretmez, yalnızca Paid/Partial hesaplar. Bu ayrım bilinçli: diyagramda "OVERDUE -> PARTIAL:
// kısmi ödeme girildi" var - yani vadesi geçmiş bir kayda kısmi ödeme gelince Overdue'da
// kalmaz, Partial'a döner (tek metotta "hâlâ vade geçmiş" kontrolü bunu bozardı).
public class Receivable
{
    public Guid Id { get; private set; }
    public Guid EnrollmentId { get; private set; }
    public Guid FeePlanId { get; private set; }
    public Guid PriceListItemId { get; private set; }
    public string Period { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public DateOnly DueDate { get; private set; }
    public ReceivableStatus Status { get; private set; } = ReceivableStatus.Unpaid;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Receivable() { }

    public static Receivable Create(
        Guid enrollmentId, Guid feePlanId, Guid priceListItemId, string period,
        decimal amount, string currency, DateOnly dueDate, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(period)) throw new ArgumentException("Dönem boş olamaz.", nameof(period));
        if (amount < 0) throw new ArgumentException("Tutar negatif olamaz.", nameof(amount));

        return new Receivable
        {
            Id = Guid.NewGuid(),
            EnrollmentId = enrollmentId,
            FeePlanId = feePlanId,
            PriceListItemId = priceListItemId,
            Period = period.Trim(),
            Amount = amount,
            Currency = currency,
            DueDate = dueDate,
            Status = ReceivableStatus.Unpaid,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    // Ödeme kaydından sonra çağrılır - yalnızca ödenen tutara bakar, vadeye bakmaz.
    public void RecordPaymentEffect(decimal totalPaid, DateTimeOffset now)
    {
        if (Status == ReceivableStatus.Cancelled) return;

        var newStatus = totalPaid >= Amount
            ? ReceivableStatus.Paid
            : totalPaid > 0 ? ReceivableStatus.Partial : ReceivableStatus.Unpaid;

        if (newStatus != Status)
        {
            Status = newStatus;
            UpdatedAt = now;
        }
    }

    // Yalnızca gecelik OverdueReceivableSweeper tarafından çağrılır (docs/05-state-models.md:
    // "OVERDUE türetilmiş bir görünüm değil, saklanan bir durumdur").
    public void MarkOverdueIfPastDue(DateOnly today, DateTimeOffset now)
    {
        if (Status is ReceivableStatus.Unpaid or ReceivableStatus.Partial && DueDate < today)
        {
            Status = ReceivableStatus.Overdue;
            UpdatedAt = now;
        }
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status == ReceivableStatus.Paid)
            throw new ConflictException("Ödenmiş bir aidat iptal edilemez.");

        Status = ReceivableStatus.Cancelled;
        UpdatedAt = now;
    }
}
