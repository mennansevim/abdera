namespace Abdera.Api.Modules.Attendance.Domain;

// docs/03-erd.md - Attendance > lesson_rsvps. UNIQUE (lesson_id, guardian_id) - bir veli bir
// ders için tek RSVP kaydına sahiptir, cevap değişirse bu kayıt güncellenir.
public class LessonRsvp
{
    public Guid Id { get; private set; }
    public Guid LessonId { get; private set; }
    public Guid GuardianId { get; private set; }
    public RsvpResponse Response { get; private set; } = RsvpResponse.Unknown;
    public DateTimeOffset? RespondedAt { get; private set; }
    public RsvpSource Source { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private LessonRsvp() { }

    public static LessonRsvp Create(Guid lessonId, Guid guardianId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        LessonId = lessonId,
        GuardianId = guardianId,
        Response = RsvpResponse.Unknown,
        Source = RsvpSource.Admin,
        CreatedAt = now,
    };

    // docs/05-state-models.md: UNKNOWN -> ATTENDING/NOT_ATTENDING, ve veli fikir değiştirirse
    // ikisi arasında serbestçe geçiş yapılabilir.
    public void Respond(RsvpResponse response, RsvpSource source, DateTimeOffset now)
    {
        Response = response;
        Source = source;
        RespondedAt = now;
    }
}
