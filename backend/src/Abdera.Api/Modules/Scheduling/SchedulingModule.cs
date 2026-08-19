using Abdera.Api.Modules.Scheduling.Features;

namespace Abdera.Api.Modules.Scheduling;

public static class SchedulingModule
{
    public static void MapSchedulingModule(this WebApplication app)
    {
        app.MapTeacherAvailabilities();
        app.MapTeacherTimeOffs();
        app.MapSchoolCalendarDays();
        app.MapLessonSeries();
        app.MapCalendar();
    }
}
