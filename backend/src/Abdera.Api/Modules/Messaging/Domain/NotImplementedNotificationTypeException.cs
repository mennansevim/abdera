namespace Abdera.Api.Modules.Messaging.Domain;

// ARC-2 (docs/13-audit-fix-prompt.md): NotificationJobType.Birthday ve PackageEnding hiçbir
// use-case tarafından üretilmiyor (bilinçli eksik, bkz. docs/05-state-models.md) - ama bu tip
// bir job her nasılsa oluşursa NotificationMessageBuilder.BuildAsync eskiden sessizce null
// dönüyordu, dispatcher da bunu "ilgili kayıt bulunamıyor" gibi yanıltıcı bir hatayla
// FAILED'a düşürüyordu. Bu istisna, dispatcher'da yakalanıp panelde okunur bir LastError
// yazmak için kullanılır - bkz. NotificationDispatcher.SendOneAsync.
public class NotImplementedNotificationTypeException(NotificationJobType type)
    : Exception($"'{type}' bildirim tipi henüz uygulanmadı.")
{
    public NotificationJobType Type { get; } = type;
}
