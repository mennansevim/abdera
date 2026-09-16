namespace Abdera.Api.Modules.Billing.Domain;

public enum PaymentMethod
{
    Cash,
    Transfer,
    Card,
    Other,
}

// docs/03-erd.md - Billing > payments. Mali kayıt - asla silinmez (CLAUDE.md).
public class Payment
{
    public Guid Id { get; private set; }
    public Guid ReceivableId { get; private set; }
    public decimal Amount { get; private set; }
    public DateOnly PaymentDate { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Reference { get; private set; }
    public string? Note { get; private set; }
    // Yıl başı peşin ödeme kampanyasının parçalarını birbirine bağlar. Her ayın muhasebe
    // kaydı ayrı kalır (aidat ayrı, ödeme ayrı); arayüz ise bunların tek bir tahsilat
    // olduğunu güvenilir biçimde gösterebilir.
    public Guid? PrepayPlanId { get; private set; }
    public int? PrepayPlanMonths { get; private set; }
    // Nullable: docs/10-decisions.md E1 - banka entegrasyonunun otomatik eşleştirdiği
    // ödemelerde bir admin yok (AuditLog.ActorUserId'nin sistem-olayları null işaretlemesiyle
    // aynı kural, bkz. guardian.opted_out).
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Payment() { }

    public static Payment Create(
        Guid receivableId, decimal amount, DateOnly paymentDate, PaymentMethod method,
        string? reference, string? note, Guid? createdBy, DateTimeOffset now,
        Guid? prepayPlanId = null, int? prepayPlanMonths = null)
    {
        if (amount <= 0) throw new ArgumentException("Ödeme tutarı pozitif olmalı.", nameof(amount));
        if (prepayPlanId.HasValue && prepayPlanMonths is < 2 or > 24)
            throw new ArgumentOutOfRangeException(nameof(prepayPlanMonths), "Peşin ödeme 2 ile 24 ay arasında olmalı.");
        if (!prepayPlanId.HasValue && prepayPlanMonths.HasValue)
            throw new ArgumentException("Ay sayısı yalnızca peşin ödemelerde belirtilebilir.", nameof(prepayPlanMonths));

        return new Payment
        {
            Id = Guid.NewGuid(),
            ReceivableId = receivableId,
            Amount = amount,
            PaymentDate = paymentDate,
            Method = method,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            PrepayPlanId = prepayPlanId,
            PrepayPlanMonths = prepayPlanMonths,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }
}
