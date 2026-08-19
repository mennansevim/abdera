using Abdera.Api.Modules.Attendance.Domain;

namespace Abdera.Tests.Unit;

public class AttendanceDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LessonRsvp_Create_starts_as_unknown_with_admin_source()
    {
        var rsvp = LessonRsvp.Create(Guid.NewGuid(), Guid.NewGuid(), Now);

        Assert.Equal(RsvpResponse.Unknown, rsvp.Response);
        Assert.Null(rsvp.RespondedAt);
    }

    [Fact]
    public void LessonRsvp_Respond_can_flip_between_attending_and_not_attending()
    {
        var rsvp = LessonRsvp.Create(Guid.NewGuid(), Guid.NewGuid(), Now);

        rsvp.Respond(RsvpResponse.Attending, RsvpSource.WhatsApp, Now.AddHours(1));
        Assert.Equal(RsvpResponse.Attending, rsvp.Response);

        rsvp.Respond(RsvpResponse.NotAttending, RsvpSource.WhatsApp, Now.AddHours(2));
        Assert.Equal(RsvpResponse.NotAttending, rsvp.Response);
    }

    [Fact]
    public void LessonAttendance_Correct_overwrites_status_and_marker()
    {
        var teacherA = Guid.NewGuid();
        var teacherB = Guid.NewGuid();
        var attendance = LessonAttendance.Create(Guid.NewGuid(), AttendanceStatus.Present, teacherA, null, Now);

        attendance.Correct(AttendanceStatus.Absent, teacherB, "yanlış girilmiş", Now.AddMinutes(10));

        Assert.Equal(AttendanceStatus.Absent, attendance.Status);
        Assert.Equal(teacherB, attendance.MarkedByTeacherId);
        Assert.Equal("yanlış girilmiş", attendance.Note);
    }
}
