namespace Abdera.Api.Shared;

// SEC-1/SEC-2 (docs/13-audit-fix-prompt.md): WhatsApp__AppSecret ve WhatsApp__PayloadSigningKey
// tanımsız kalırsa webhook imza doğrulaması/RSVP buton imzası artık fail-closed davranıyor
// (bkz. WebhookSignatureVerifier.IsValid, RsvpButtonPayload.TryVerify) - ama "her isteği
// sessizce reddet" bir prod ortamında fark edilmeden uzun süre sürebilir (WhatsApp
// entegrasyonu tamamen çalışmaz hale gelir, kimse haberdar olmaz). Production'da bu
// anahtarlar eksikse uygulama hiç ayağa kalkmasın, hata erken ve açık olsun.
// Development'ta zorunlu değil - Fake WhatsApp sağlayıcısı bu anahtarları hiç kullanmaz.
public static class ProductionSecretsGuard
{
    public static void EnsureConfigured(WebApplication app)
    {
        if (!app.Environment.IsProduction())
        {
            return;
        }

        var missing = new List<string>();
        if (string.IsNullOrEmpty(app.Configuration["WhatsApp:AppSecret"]))
        {
            missing.Add("WhatsApp__AppSecret");
        }
        if (string.IsNullOrEmpty(app.Configuration["WhatsApp:PayloadSigningKey"]))
        {
            missing.Add("WhatsApp__PayloadSigningKey");
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Production ortamında zorunlu ortam değişkenleri tanımsız: {string.Join(", ", missing)}. " +
                "Bunlar boşken webhook imza doğrulaması/RSVP buton imzası sessizce fail-closed olur " +
                "(WhatsApp entegrasyonu tamamen işlevsiz kalır) - uygulama bu yüzden başlamayı reddediyor.");
        }
    }
}
