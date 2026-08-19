using Abdera.Api.Modules.Attendance.Features;

namespace Abdera.Api.Modules.Attendance;

public static class AttendanceModule
{
    public static void MapAttendanceModule(this WebApplication app)
    {
        app.MapRsvp();
        app.MapMarkAttendance();
    }
}
