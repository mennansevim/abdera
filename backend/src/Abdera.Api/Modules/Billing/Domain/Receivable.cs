using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Billing.Domain;

// docs/03-erd.md - Billing > receivables. Bir kurs kaydının bir aylık aidatı.
//
// Yeniden tasarım (docs/10-decisions.md H1): eskiden fee_plan_id + price_list_item_id
// üzerinden iki ara katmana bağlıydı; artık hesabın TAMAMINI kendi içinde taşıyor -
// taban tutar, uygulanan indirim yüzdesi, indirimin gerekçesi ve net tutar. Bir aidat
// satırına bakan admin "bu 5.700 TL nereden geldi" sorusunu başka tabloya gitmeden
// yanıtlayabiliyor; tarife sonradan değişse bile bu satır değişmiyor (fiyat snapshot'ı, A1).
//
// docs/05-state-models.md: OVERDUE'ya giden tek yol gecelik sweeper'dır (Unpaid/Partial +
// vade geçmiş) - ödeme kaydı asla doğrudan Overdue üretmez, yalnızca Paid/Partial hesaplar.
// Bu ayrım bilinçli: diyagramda "OVERDUE -> PARTIAL: kısmi ödeme girildi" var - yani vadesi
// geçmiş bir kayda kısmi ödeme gelince Overdue'da kalmaz, Partial'a döner.
public class Receivable
{
    public Guid Id { get; private set; }
    public Guid EnrollmentId { get; private set; }
    // Hangi tarifeden türediği - izlenebilirlik için. Tarife silinmez, kapatılır.
    public Guid TuitionRateId { get; private set; }
    public string Period { get; private set; } = null!;
    // İndirimden önceki tarife tutarı.
    public decimal BaseAmount { get; private set; }
    // Uygulanan indirimin bileşik yüzdesi (öğrenci indirimi + peşin ödeme indirimi
    // birlikte). Gösterim ve denetim içindir; tahsilat her zaman Amount ile karşılaştırılır.
    public decimal DiscountPercent { get; private set; }
    public string? DiscountReason { get; private set; }
    // Tahsil edilecek net tutar. Ödeme bununla karşılaştırılır.
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public DateOnly DueDate { get; private set; }
    public ReceivableStatus Status { get; private set; } = ReceivableStatus.Unpaid;
    // Yıl başı toplu ödeme kampanyasında birlikte üretilen ayları birbirine bağlar.
    public Guid? PrepayPlanId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Receivable() { }

    public static Receivable Create(
        Guid enrollmentId, Guid tuitionRateId, string period, TuitionCalculator.Breakdown breakdown,
        string currency, DateOnly dueDate, DateTimeOffset now, Guid? prepayPlanId = null)
    {
        if (string.IsNullOrWhiteSpace(period)) throw new ArgumentException("Dönem boş olamaz.", nameof(period));
        if (breakdown.NetAmount < 0) throw new ArgumentException("Tutar negatif olamaz.", nameof(breakdown));
        if (breakdown.BaseAmount < 0) throw new ArgumentException("Taban tutar negatif olamaz.", nameof(breakdown));

        return new Receivable
        {
            Id = Guid.NewGuid(),
            EnrollmentId = enrollmentId,
            TuitionRateId = tuitionRateId,
            Period = period.Trim(),
            BaseAmount = breakdown.BaseAmount,
            DiscountPercent = breakdown.DiscountPercent,
            DiscountReason = breakdown.DiscountReason,
            Amount = breakdown.NetAmount,
            Currency = currency,
            DueDate = dueDate,
            Status = ReceivableStatus.Unpaid,
            PrepayPlanId = prepayPlanId,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    // Peşin ödeme kampanyası, daha önce normal tarifeyle açılmış bir ayı da kapsayabilir.
    // O ay iptal edilip yenisi açılmaz - tutarı kampanya oranıyla yeniden hesaplanır.
    // Üzerine ödeme girilmiş bir aidat bu yolla değiştirilemez: tahsil edilmiş parayla
    // tutarsız bir satır üretmek, eksik/fazla bakiyeyi sessizce gizlerdi.
    public void Reprice(TuitionCalculator.Breakdown breakdown, Guid? prepayPlanId, DateTimeOffset now)
    {
        if (Status == ReceivableStatus.Cancelled)
            throw new ConflictException("İptal edilmiş bir aidatın tutarı değiştirilemez.");
        if (Status is ReceivableStatus.Paid or ReceivableStatus.Partial)
            throw new ConflictException("Üzerinde ödeme bulunan bir aidatın tutarı değiştirilemez.");

        BaseAmount = breakdown.BaseAmount;
        DiscountPercent = breakdown.DiscountPercent;
        DiscountReason = breakdown.DiscountReason;
        Amount = breakdown.NetAmount;
        PrepayPlanId = prepayPlanId ?? PrepayPlanId;
        UpdatedAt = now;
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
