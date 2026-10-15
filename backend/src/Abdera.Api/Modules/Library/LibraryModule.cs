using Abdera.Api.Modules.Library.Features;
using Abdera.Api.Shared.Modules;

namespace Abdera.Api.Modules.Library;

public static class LibraryModule
{
    public static void MapLibraryModule(this WebApplication app)
    {
        var module = app.MapModule(ModuleKeys.Library);
        module.MapScoreFiles();
        module.MapLibraryPieces();
        module.MapLibrarySuggestions();
    }
}
