using Abdera.Api.Modules.Library.Features;

namespace Abdera.Api.Modules.Library;

public static class LibraryModule
{
    public static void MapLibraryModule(this WebApplication app)
    {
        app.MapScoreFiles();
        app.MapLibraryPieces();
        app.MapLibrarySuggestions();
    }
}
