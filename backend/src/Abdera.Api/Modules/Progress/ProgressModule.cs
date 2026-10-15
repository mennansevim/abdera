using Abdera.Api.Modules.Progress.Features;
using Abdera.Api.Modules.Progress.Infrastructure;
using Abdera.Api.Shared.Modules;

namespace Abdera.Api.Modules.Progress;

public static class ProgressModule
{
    public static void AddProgressModule(this IServiceCollection services)
    {
        services.AddHostedService<LessonNoteReminderWorker>();
    }

    public static void MapProgressModule(this WebApplication app)
    {
        // Ders notu öğretmenin günlük akışının (yoklama, telafi) parçası - çekirdek kalır.
        // Gelişim ekranı, beceri, ödev, pratik günlüğü ve karne "progress" modülüdür.
        app.MapLessonNotes();
        app.MapPendingLessonNotes();

        var module = app.MapModule(ModuleKeys.Progress);
        module.MapStudentProgress();
        module.MapStudentProgressSummary();
        module.MapSkillAssessments();
        module.MapPracticeAssignments();
        module.MapPracticeJournal();
        module.MapTermReports();
    }
}
