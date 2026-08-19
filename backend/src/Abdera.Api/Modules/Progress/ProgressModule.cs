using Abdera.Api.Modules.Progress.Features;

namespace Abdera.Api.Modules.Progress;

public static class ProgressModule
{
    public static void MapProgressModule(this WebApplication app)
    {
        app.MapLessonNotes();
    }
}
