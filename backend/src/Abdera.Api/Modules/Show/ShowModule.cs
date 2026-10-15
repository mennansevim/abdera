using Abdera.Api.Modules.Show.Features;
using Abdera.Api.Shared.Modules;

namespace Abdera.Api.Modules.Show;

public static class ShowModule
{
    public static void MapShowModule(this WebApplication app)
    {
        var module = app.MapModule(ModuleKeys.Show);
        module.MapShows();
        module.MapShowStage();
        module.MapStudentPhotos();
    }
}
