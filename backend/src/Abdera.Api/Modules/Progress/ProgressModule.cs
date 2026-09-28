using Abdera.Api.Modules.Progress.Features;
using Abdera.Api.Modules.Progress.Infrastructure;

namespace Abdera.Api.Modules.Progress;

public static class ProgressModule
{
    public static void AddProgressModule(this IServiceCollection services, bool enableHostedServices = true)
    {
        if (enableHostedServices)
        {
            services.AddHostedService<LessonNoteReminderWorker>();
        }
    }

    public static void MapProgressModule(this WebApplication app)
    {
        app.MapLessonNotes();
        app.MapPendingLessonNotes();
        app.MapLessonNoteReminderCron();
        app.MapStudentProgress();
        app.MapStudentProgressSummary();
        app.MapSkillAssessments();
        app.MapPracticeAssignments();
        app.MapPracticeJournal();
    }
}
