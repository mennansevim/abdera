using Abdera.Api.Modules.Banking.Domain;

namespace Abdera.Tests.Unit;

// Gerçek bir prod bug'ının regresyon testi: ProductionSecretsGuard "Banking__Provider Fake
// OLMASIN" diye tutturuyordu, Program.cs ise "Fake DIŞINDA her şeye throw" ediyordu. Yani
// Production'da hiçbir değer çalışmıyor, uygulama hiç ayağa kalkamıyordu.
//
// Guard'ı izole test eden birim testi bunu göremedi - ProductionSecretsGuardTests kendi
// "eksiksiz production config"inde Banking:Provider = "ConfiguredBank" kullanıyordu ve guard
// bunu memnuniyetle kabul ediyordu; oysa aynı değer gerçek startup'ta Program.cs'te patlardı.
//
// Asıl invariant iki kod noktası ARASINDA: production'da izin verilen her değer, DI
// tarafından da kaydedilebilir olmak zorunda. Aşağıdaki test tam olarak bunu bekçiliyor.
public class BankingProviderModesTests
{
    // Program.cs'in gerçekten bir IBankPaymentProvider kaydedebildiği değerler.
    // Yeni bir sağlayıcı eklenirse hem BankingProviderModes.IsSupported hem burası güncellenir.
    private static readonly string[] KnownModes = [BankingProviderModes.Fake, BankingProviderModes.Manual];

    [Fact]
    public void Every_production_allowed_value_is_also_supported_by_dependency_injection()
    {
        foreach (var mode in KnownModes.Where(BankingProviderModes.IsAllowedInProduction))
        {
            Assert.True(
                BankingProviderModes.IsSupported(mode),
                $"'{mode}' production'da kabul ediliyor ama Program.cs bunun için bir IBankPaymentProvider kaydedemiyor - uygulama Production'da hiç ayağa kalkamaz.");
        }
    }

    [Fact]
    public void At_least_one_mode_lets_the_application_boot_in_production()
    {
        // Bu iddia olmadan yukarıdaki test boş bir kümede de "geçer": production'da izin
        // verilen hiçbir değer kalmazsa döngü hiç dönmez ve bug yeniden sızabilir.
        Assert.Contains(KnownModes, mode =>
            BankingProviderModes.IsAllowedInProduction(mode) && BankingProviderModes.IsSupported(mode));
    }

    [Theory]
    [InlineData("Fake")]
    [InlineData("fake")]
    [InlineData("")]
    [InlineData(null)]
    public void Fake_and_empty_are_rejected_in_production(string? value)
    {
        Assert.False(BankingProviderModes.IsAllowedInProduction(value));
    }

    [Theory]
    [InlineData("Manual")]
    [InlineData("manual")]
    public void Manual_is_a_valid_production_choice(string value)
    {
        Assert.True(BankingProviderModes.IsAllowedInProduction(value));
        Assert.True(BankingProviderModes.IsSupported(value));
    }

    [Theory]
    [InlineData("Fake")]
    [InlineData("Manual")]
    public void Modes_without_a_real_bank_do_not_require_a_webhook_secret(string value)
    {
        Assert.False(BankingProviderModes.UsesWebhooks(value));
    }

    [Fact]
    public void An_unknown_provider_name_is_not_silently_supported()
    {
        // Gerçek sağlayıcı seçilmeden önce yanlışlıkla "PayTR" yazılırsa DI bunu tanımamalı;
        // Program.cs açık bir hata fırlatır, sessizce Fake'e düşmez.
        Assert.False(BankingProviderModes.IsSupported("PayTR"));
    }
}
