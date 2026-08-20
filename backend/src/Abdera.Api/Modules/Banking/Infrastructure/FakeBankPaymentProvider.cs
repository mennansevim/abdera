using Abdera.Api.Modules.Banking.Domain;

namespace Abdera.Api.Modules.Banking.Infrastructure;

// Banking__Provider=Fake (dev/test varsayılanı). Gerçek bir API çağrısı yapmaz; gerçekçi
// görünen ama sahte bir TR IBAN üretir. Gelen işlem bildirimleri gerçek sağlayıcının
// webhook'u yerine POST /api/dev/bank/simulate-transaction (yalnızca Development) ile
// taklit edilir - bkz. docs/12-bank-integration.md.
public class FakeBankPaymentProvider(ILogger<FakeBankPaymentProvider> logger) : IBankPaymentProvider
{
    public Task<VirtualIbanAllocationResult> AllocateVirtualIbanAsync(Guid guardianId, CancellationToken cancellationToken = default)
    {
        var fakeIban = $"TR{Random.Shared.NextInt64(100000000000000, 999999999999999)}";
        var providerReference = $"fake-viban-{Guid.NewGuid()}";

        logger.LogInformation("[FakeBank] sanal IBAN tahsis edildi -> veli={GuardianId} iban={Iban}", guardianId, fakeIban);

        return Task.FromResult(new VirtualIbanAllocationResult(true, fakeIban, "Fake", providerReference, null));
    }
}
