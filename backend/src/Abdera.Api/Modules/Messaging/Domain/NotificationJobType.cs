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
}

public enum NotificationJobStatus
{
    Pending,
    Processing,
    Sent,
    Failed,
    Cancelled,
}
