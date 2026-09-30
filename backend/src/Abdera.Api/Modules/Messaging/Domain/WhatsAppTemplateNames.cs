namespace Abdera.Api.Modules.Messaging.Domain;

// Modüller arası paylaşılan şablon adları. Yalnızca birden fazla modülün bilmesi gereken adlar
// buradadır; bildirim job'larının şablonları NotificationMessageBuilder'da kalır.
public static class WhatsAppTemplateNames
{
    // Öğrenci ilk kaydedildiğinde veliye giden tek seferlik karşılama mesajı: panel adresi ve
    // "şifrenizi okul yönetiminden öğrenebilirsiniz". Şifre İÇERMEZ, bu yüzden Utility
    // kategorisinde onaylanır. Onay kapalıyken de bir kez gider - bkz. ConsentGatedWhatsAppClient (R2).
    public const string GuardianWelcome = "welcome_student";
}
