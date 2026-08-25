namespace Abdera.Api.Modules.Billing.Domain;

// Ödeme kayıtları değiştirilmez veya silinmez. Bir düzeltme, önceki etkin tutarı ve yeni
// etkin tutarı ayrı bir mali olay olarak saklar; böylece hesap bakiyesi güncellenirken
// özgün tahsilat ve tüm düzeltme zinciri izlenebilir kalır.
public class PaymentCorrection
{
    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public decimal PreviousAmount { get; private set; }
    public decimal CorrectedAmount { get; private set; }
    public string Reason { get; private set; } = null!;
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PaymentCorrection() { }

    public static PaymentCorrection Create(
        Guid paymentId,
        decimal previousAmount,
        decimal correctedAmount,
        string reason,
        Guid createdBy,
        DateTimeOffset now)
    {
        if (previousAmount < 0) throw new ArgumentOutOfRangeException(nameof(previousAmount));
        if (correctedAmount < 0) throw new ArgumentOutOfRangeException(nameof(correctedAmount));
        if (previousAmount == correctedAmount) throw new ArgumentException("Düzeltilen tutar mevcut tutardan farklı olmalı.", nameof(correctedAmount));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Düzeltme nedeni zorunludur.", nameof(reason));

        return new PaymentCorrection
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            PreviousAmount = previousAmount,
            CorrectedAmount = correctedAmount,
            Reason = reason.Trim(),
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }
}
