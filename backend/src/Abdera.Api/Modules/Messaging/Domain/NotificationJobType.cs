namespace Abdera.Api.Modules.Messaging.Domain;

// docs/03-erd.md - Messaging > notification_jobs.type. LessonReminder/LessonRescheduled/
// MakeupApproved/PaymentReminder Phase 5'te tetikleniyor; Birthday/PackageEnding Phase 6'nın
// cron kaynaklı işi (docs/00-master-prompt.md Phase 6) - enum değeri burada hazır bekliyor
// ama henüz hiçbir use-case bunları üretmiyor.
public enum NotificationJobType
{
    LessonReminder,
    LessonRescheduled,
    MakeupApproved,
    PaymentReminder,
    Birthday,
    PackageEnding,
    InstrumentMaintenance,
    // Haftalık ders programı (lesson_series) yeni gün/saate taşındı. Referans YENİ seridir
    // (reference_type="lesson_series"): her taşıma yeni bir seri açtığı için doğal idempotency
    // anahtarı. Olay tetiklemeli, sessiz saate tabi değil. Meta'da onaylı lesson_rescheduled
    // şablonuyla gider (NotificationMessageBuilder.BuildScheduleChangeMessageAsync).
    LessonScheduleChanged,
    // Veli "gelemiyor"/"gecikecek" dedi (ya da olumsuz yanıtını "geliyor"a çevirdi): dersin
    // öğretmenine WhatsApp (docs/10-decisions.md AK). Alıcı veli DEĞİL öğretmendir
    // (teachers.phone_number). Referans ders (reference_type="lesson"): ders başına tek satır,
    // yanıt yeniden değişince aynı satır yeniden kuyruğa alınır; ders iptal/taşınınca A4 ile iptal.
    // Olay tetiklemeli, sessiz saate tabi değil. Mesaj gönderim anındaki yanıtı yazar.
    TeacherRsvpAlert,
}

// Alıcısı veli olmayan job tipleri. Dispatcher bunlarda veli/rıza aramaz, alıcının aktif bir
// öğretmen olduğunu doğrular; whatsapp_messages (veli konuşma geçmişi) satırı yazılmaz.
public static class NotificationJobRecipients
{
    public static bool IsTeacher(NotificationJobType type) => type == NotificationJobType.TeacherRsvpAlert;
}

public enum NotificationJobStatus
{
    Pending,
    Processing,
    Sent,
    Failed,
    Cancelled,
}
