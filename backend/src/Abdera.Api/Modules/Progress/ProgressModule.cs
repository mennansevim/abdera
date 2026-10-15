using Abdera.Api.Modules.Progress.Features;
using Abdera.Api.Modules.Progress.Infrastructure;

namespace Abdera.Api.Modules.Progress;

public static class ProgressModule
{
    public static void AddProgressModule(this IServiceCollection services)
    {
        services.AddHostedService<LessonNoteReminderWorker>();
    }

    public static void MapProgressModule(this WebApplication app)
    {
        app.MapLessonNotes();
        app.MapPendingLessonNotes();
        app.MapStudentProgress();
        app.MapStudentProgressSummary();
        app.MapSkillAssessments();
        app.MapPracticeAssignments();
        app.MapPracticeJournal();
        app.MapTermReports();
    }
}
