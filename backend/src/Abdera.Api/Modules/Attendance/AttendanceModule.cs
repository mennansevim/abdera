using Abdera.Api.Modules.Attendance.Features;
using Abdera.Api.Modules.Attendance.Infrastructure;

namespace Abdera.Api.Modules.Attendance;

public static class AttendanceModule
{
    public static void AddAttendanceModule(this IServiceCollection services, bool enableHostedServices = true)
    {
        if (enableHostedServices)
        {
            services.AddHostedService<AttendanceReminderWorker>();
        }
    }

    public static void MapAttendanceModule(this WebApplication app)
    {
        app.MapRsvp();
        app.MapMarkAttendance();
        app.MapAttendanceHistory();
    }
}
