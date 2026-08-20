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
    // Nullable: docs/10-decisions.md E1 - banka entegrasyonunun otomatik eşleştirdiği
    // ödemelerde bir admin yok (AuditLog.ActorUserId'nin sistem-olayları null işaretlemesiyle
    // aynı kural, bkz. guardian.opted_out).
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Payment() { }

    public static Payment Create(
        Guid receivableId, decimal amount, DateOnly paymentDate, PaymentMethod method,
        string? reference, string? note, Guid? createdBy, DateTimeOffset now)
    {
        if (amount <= 0) throw new ArgumentException("Ödeme tutarı pozitif olmalı.", nameof(amount));

        return new Payment
        {
            Id = Guid.NewGuid(),
            ReceivableId = receivableId,
            Amount = amount,
            PaymentDate = paymentDate,
            Method = method,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }
}
