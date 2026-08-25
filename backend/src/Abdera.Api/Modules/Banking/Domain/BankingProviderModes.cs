namespace Abdera.Api.Modules.Banking.Domain;

// Banking__Provider'ın geçerli değerleri TEK yerde tanımlanır.
//
// Neden ayrı bir sınıf: bu değer iki ayrı yerde, iki ayrı amaçla okunuyor -
//   1) Program.cs, Build()'DEN ÖNCE: hangi IBankPaymentProvider DI'a kaydedilecek?
//   2) ProductionSecretsGuard, Build()'DEN SONRA: bu değer production'da kabul edilebilir mi?
// İkisi birbirinden habersiz geliştiğinde uygulama hiç ayağa kalkamayan bir duruma
// düşebiliyordu: guard "Fake olmasın" diyor, Program.cs ise "Fake dışında her şeye throw"
// ediyordu - yani Production'da hiçbir değer çalışmıyordu. Guard'ı izole test eden birim
// testi bunu göremez (gerçek startup'ta patlayacak bir değeri "geçerli" sayıyordu).
//
// Bu yüzden her iki taraf da aşağıdaki iki yüklemi kullanır ve
// BankingProviderModesTests bunlar arasındaki tutarlılığı doğrular:
// production'da izin verilen HER değer, DI tarafından da desteklenmek ZORUNDA.
public static class BankingProviderModes
{
    // Sahte ama gerçekçi görünen IBAN üretir. Yalnızca dev/test - production'da bu IBAN
    // bir veliye verilse para hiçbir yere gitmez ve kimse fark etmez.
    public const string Fake = "Fake";

    // Banka entegrasyonu bilinçli olarak kapalı: sanal IBAN tahsisi açık bir hatayla
    // reddedilir, admin ödemeyi elle girer. Production'da geçerli.
    public const string Manual = "Manual";

    // Program.cs'in bir IBankPaymentProvider kaydedebildiği değerler. Gerçek sağlayıcı
    // (PayTR/Papara İşletme/banka Sanal IBAN ürünü) seçilince buraya eklenir.
    public static bool IsSupported(string? value) =>
        Matches(value, Fake) || Matches(value, Manual);

    // Production'da kabul edilebilir değerler. Fake burada bilinçli olarak yok.
    public static bool IsAllowedInProduction(string? value) =>
        IsSupported(value) && !Matches(value, Fake);

    // Gerçek bir banka sağlayıcısı mı (webhook, imza doğrulama, otomatik eşleştirme var mı)?
    // Manual ve Fake için hayır - bu yüzden onlarda Banking__WebhookSharedSecret beklenmez.
    public static bool UsesWebhooks(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !Matches(value, Fake) && !Matches(value, Manual);

    private static bool Matches(string? value, string mode) =>
        string.Equals(value, mode, StringComparison.OrdinalIgnoreCase);
}
