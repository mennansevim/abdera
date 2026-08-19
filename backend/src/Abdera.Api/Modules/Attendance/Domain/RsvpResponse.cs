namespace Abdera.Api.Modules.Attendance.Domain;

// docs/05-state-models.md - LessonRsvp.response. ATTENDING/NOT_ATTENDING velinin niyetini
// ifade eder, gerçek yoklamayı değil (bkz. LessonAttendance) - bu ikisi asla birleştirilmez.
public enum RsvpResponse
{
    Unknown,
    Attending,
    NotAttending,
}

public enum RsvpSource
{
    WhatsApp,
    Admin,
}
