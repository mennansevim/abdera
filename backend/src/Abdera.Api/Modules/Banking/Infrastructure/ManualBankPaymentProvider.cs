using Abdera.Api.Modules.Banking.Domain;

namespace Abdera.Api.Modules.Banking.Infrastructure;

// Banking__Provider=Manual - production'da geçerli, "banka entegrasyonu yok" modu.
//
// docs/10-decisions.md E1'in onayladığı sanal IBAN sağlayıcısı (PayTR/Papara İşletme/banka
// ürünü) henüz seçilmedi. Bu seçim yapılmadan okul canlıya çıkamasın diye beklemek gereksiz:
// aidat takibi, tahsilat, kısmi ödeme ve düzeltme akışlarının hiçbiri bankaya bağlı değil -
// yalnızca "gelen havalenin otomatik Receivable'a işlenmesi" devre dışı kalır, admin ödemeyi
// elle girer (Payments.cs).
//
// FakeBankPaymentProvider'dan farkı kritik: Fake, gerçekçi GÖRÜNEN ama sahte bir IBAN üretir.
// O IBAN production'da bir veliye verilse para hiçbir yere gitmez ve kimse fark etmez.
// Manual bunun yerine tahsisi açık bir hata mesajıyla reddeder - sessiz başarısızlık yerine
// görünür ret (aynı fail-closed gerekçesi: ProductionSecretsGuard.cs).
public class ManualBankPaymentProvider(ILogger<ManualBankPaymentProvider> logger) : IBankPaymentProvider
{
    public const string ProviderName = "Manual";

    private const string UnavailableMessage =
        "Sanal IBAN tahsisi kapalı: okul için bir banka sağlayıcısı yapılandırılmamış " +
        "(Banking__Provider=Manual). Ödemeler aidat ekranından elle kaydedilir.";

    public Task<VirtualIbanAllocationResult> AllocateVirtualIbanAsync(Guid guardianId, CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "[ManualBank] sanal IBAN tahsisi istendi ama banka sağlayıcısı yapılandırılmamış -> veli={GuardianId}",
            guardianId);

        return Task.FromResult(new VirtualIbanAllocationResult(false, null, ProviderName, null, UnavailableMessage));
    }
}
