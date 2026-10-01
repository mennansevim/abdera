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
}

public enum NotificationJobStatus
{
    Pending,
    Processing,
    Sent,
    Failed,
    Cancelled,
}
