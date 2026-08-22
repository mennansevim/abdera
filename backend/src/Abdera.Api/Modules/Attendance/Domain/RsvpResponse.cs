namespace Abdera.Api.Modules.Attendance.Domain;

// docs/05-state-models.md - LessonRsvp.response. ATTENDING/ATTENDING_LATE/NOT_ATTENDING velinin
// niyetini ifade eder, gerçek yoklamayı değil (bkz. LessonAttendance) - bu ikisi asla birleştirilmez.
// AttendingLate (Faz 3): "Evet ama biraz gecikeceğim" - geliyor ama zamanında değil; yoklama
// tarafında hâlâ normal bir "geldi" kaydı gerektirir, yalnızca velinin niyetini ayrıştırır.
public enum RsvpResponse
{
    Unknown,
    Attending,
    AttendingLate,
    NotAttending,
}

public enum RsvpSource
{
    WhatsApp,
    Admin,
    // Veli, /parent portalına OTP ile giriş yapıp kendi RSVP'sini kendisi ayarladığında
    // (Modules/People/Features/GuardianPortal.cs) - docs/10-decisions.md Karar F reversal.
    GuardianWeb,
}
