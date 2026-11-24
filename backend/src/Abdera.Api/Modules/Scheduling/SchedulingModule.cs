using Abdera.Api.Modules.Scheduling.Features;
using Abdera.Api.Modules.Scheduling.Infrastructure;

namespace Abdera.Api.Modules.Scheduling;

public static class SchedulingModule
{
    public static void AddSchedulingModule(this IServiceCollection services)
    {
        services.AddHostedService<SeriesExtensionWorker>();
    }

    public static void MapSchedulingModule(this WebApplication app)
    {
        app.MapTeacherAvailabilities();
        app.MapTeacherTimeOffs();
        app.MapSchoolCalendarDays();
        app.MapLessonSeries();
        app.MapLessonGroups();
        app.MapCalendar();
        app.MapChangeRequests();
        app.MapCancelLesson();
        app.MapUpdateLesson();
        app.MapSwapLessons();
        app.MapOpenSlots();
    }
}
