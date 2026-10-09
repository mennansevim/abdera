namespace Abdera.Api.Modules.Billing.Domain;

// Ödeme kaydı değiştirilmez (PaymentCorrection ile aynı ilke): yanlış girilmiş ödeme tarihi -
// tipik olarak geçen ay alınan paranın tahsilat penceresinin varsayılanı olan "bugün" ile
// sisteme işlenmesi - ayrı bir olay olarak düzeltilir. Etkin tarih en son düzeltmedir; gelir
// ekranı parayı o tarihin ayına yazar.
public class PaymentDateCorrection
{
    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public DateOnly PreviousDate { get; private set; }
    public DateOnly CorrectedDate { get; private set; }
    public string Reason { get; private set; } = null!;
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private PaymentDateCorrection() { }

    public static PaymentDateCorrection Create(
        Guid paymentId,
        DateOnly previousDate,
        DateOnly correctedDate,
        string reason,
        DateOnly today,
        Guid createdBy,
        DateTimeOffset now)
    {
        if (previousDate == correctedDate) throw new ArgumentException("Düzeltilen tarih mevcut tarihten farklı olmalı.", nameof(correctedDate));
        if (correctedDate > today) throw new ArgumentOutOfRangeException(nameof(correctedDate), "Ödeme tarihi ileri bir gün olamaz.");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Düzeltme nedeni zorunludur.", nameof(reason));

        return new PaymentDateCorrection
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            PreviousDate = previousDate,
            CorrectedDate = correctedDate,
            Reason = reason.Trim(),
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }
}
