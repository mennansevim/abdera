namespace Abdera.Api.Modules.Messaging.Domain;

// Modüller arası paylaşılan şablon adları. Yalnızca birden fazla modülün bilmesi gereken adlar
// buradadır; bildirim job'larının şablonları NotificationMessageBuilder'da kalır.
public static class WhatsAppTemplateNames
{
    // Veliye giriş bilgilerini (şifre) ileten şablon. Öğrenci ilk kaydedildiğinde gönderilen
    // tek seferlik karşılama mesajı da budur - bkz. ConsentGatedWhatsAppClient (R2).
    public const string GuardianPassword = "guardian_password";
}
