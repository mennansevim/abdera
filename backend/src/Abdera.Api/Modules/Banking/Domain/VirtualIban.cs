using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Banking.Domain;

public enum VirtualIbanStatus
{
    Active,
    Inactive,
}

// docs/12-bank-integration.md - bir veliye atanan sanal IBAN. Sağlayıcıdan bağımsız:
// IBankPaymentProvider.AllocateVirtualIbanAsync bu IBAN'ı tahsis eder, biz yalnızca
// sonucu saklarız. Bir veliye aynı anda birden fazla Active sanal IBAN atanamaz -
// uygulama katmanında kontrol edilir (bkz. AssignVirtualIban.cs).
public class VirtualIban
{
    public Guid Id { get; private set; }
    public Guid GuardianId { get; private set; }
    public string Iban { get; private set; } = null!;
    public string Provider { get; private set; } = null!;
    public string? ProviderReference { get; private set; }
    public VirtualIbanStatus Status { get; private set; } = VirtualIbanStatus.Active;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private VirtualIban() { }

    public static VirtualIban Create(Guid guardianId, string iban, string provider, string? providerReference, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(iban)) throw new ArgumentException("IBAN boş olamaz.", nameof(iban));
        if (string.IsNullOrWhiteSpace(provider)) throw new ArgumentException("Sağlayıcı boş olamaz.", nameof(provider));

        return new VirtualIban
        {
            Id = Guid.NewGuid(),
            GuardianId = guardianId,
            Iban = iban.Trim(),
            Provider = provider.Trim(),
            ProviderReference = string.IsNullOrWhiteSpace(providerReference) ? null : providerReference.Trim(),
            Status = VirtualIbanStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (Status == VirtualIbanStatus.Inactive)
            throw new ConflictException("Sanal IBAN zaten pasif.");

        Status = VirtualIbanStatus.Inactive;
        UpdatedAt = now;
    }
}
