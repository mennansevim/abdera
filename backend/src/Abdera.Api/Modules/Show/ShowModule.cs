using Abdera.Api.Modules.Show.Features;

namespace Abdera.Api.Modules.Show;

public static class ShowModule
{
    public static void MapShowModule(this WebApplication app)
    {
        app.MapShows();
        app.MapShowStage();
        app.MapStudentPhotos();
    }
}
