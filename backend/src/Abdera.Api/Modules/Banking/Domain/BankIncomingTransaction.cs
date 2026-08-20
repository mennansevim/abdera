using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Banking.Domain;

public enum BankIncomingTransactionStatus
{
    Received,
    Matched,
    NeedsReview,
    Ignored,
}

// docs/12-bank-integration.md - sağlayıcıdan gelen her havale/EFT bildirimi burada
// tutulur, hiçbir zaman silinmez (CLAUDE.md: finansal/audit kayıt silinmez). Bir işlem
// yalnızca bir kez Matched'e geçebilir - RecordMatch/MarkNeedsReview/Ignore durum
// makinesi bunu zorlar.
public class BankIncomingTransaction
{
    public Guid Id { get; private set; }
    public Guid VirtualIbanId { get; private set; }
    public string Provider { get; private set; } = null!;
    public string ProviderTransactionId { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public string? SenderName { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public BankIncomingTransactionStatus Status { get; private set; } = BankIncomingTransactionStatus.Received;
    public Guid? MatchedReceivableId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private BankIncomingTransaction() { }

    public static BankIncomingTransaction Receive(
        Guid virtualIbanId, string provider, string providerTransactionId, decimal amount, string currency,
        string? senderName, string? description, DateTimeOffset receivedAt, DateTimeOffset now)
    {
        if (amount <= 0) throw new ArgumentException("İşlem tutarı pozitif olmalı.", nameof(amount));

        return new BankIncomingTransaction
        {
            Id = Guid.NewGuid(),
            VirtualIbanId = virtualIbanId,
            Provider = provider,
            ProviderTransactionId = providerTransactionId,
            Amount = amount,
            Currency = currency,
            SenderName = string.IsNullOrWhiteSpace(senderName) ? null : senderName.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            ReceivedAt = receivedAt,
            Status = BankIncomingTransactionStatus.Received,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void RecordMatch(Guid receivableId, DateTimeOffset now)
    {
        EnsureUnresolved();
        Status = BankIncomingTransactionStatus.Matched;
        MatchedReceivableId = receivableId;
        UpdatedAt = now;
    }

    public void MarkNeedsReview(DateTimeOffset now)
    {
        EnsureUnresolved();
        Status = BankIncomingTransactionStatus.NeedsReview;
        UpdatedAt = now;
    }

    // docs/12-bank-integration.md: admin "hiçbirine sayma" seçebilir (örn. bağış, yanlış hesap).
    public void Ignore(DateTimeOffset now)
    {
        if (Status == BankIncomingTransactionStatus.Matched)
            throw new ConflictException("Zaten eşleşmiş bir işlem yok sayılamaz.");

        Status = BankIncomingTransactionStatus.Ignored;
        UpdatedAt = now;
    }

    private void EnsureUnresolved()
    {
        if (Status is BankIncomingTransactionStatus.Matched or BankIncomingTransactionStatus.Ignored)
            throw new ConflictException($"'{Status}' durumundaki bir işlem yeniden işlenemez.");
    }
}
