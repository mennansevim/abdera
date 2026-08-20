namespace Abdera.Api.Modules.Banking.Domain;

// docs/10-decisions.md E1 / docs/12-bank-integration.md: WhatsApp'taki IWhatsAppClient/D2
// deseninin aynısı - gerçek sağlayıcı (PayTR/Papara İşletme/banka Sanal IBAN ürünü) henüz
// seçilmedi, kod FakeBankPaymentProvider ile bekletilmeden ilerler. Sağlayıcı seçilince
// yalnızca yeni bir implementasyon eklenir, çağıran kod değişmez.
public interface IBankPaymentProvider
{
    Task<VirtualIbanAllocationResult> AllocateVirtualIbanAsync(Guid guardianId, CancellationToken cancellationToken = default);
}

public record VirtualIbanAllocationResult(bool Success, string? Iban, string Provider, string? ProviderReference, string? Error);
