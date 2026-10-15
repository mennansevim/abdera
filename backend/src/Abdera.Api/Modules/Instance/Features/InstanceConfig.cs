using Abdera.Api.Modules.Instance.Domain;
using Abdera.Api.Shared;
using Abdera.Api.Shared.Modules;

namespace Abdera.Api.Modules.Instance.Features;

// Arayüzün açılışta okuduğu kurulum bilgisi: okulun kimliği ve açık modüller. Giriş ekranı da
// okul adını/logosunu göstermek için çağırdığından anonimdir; kişisel veri içermez.
public static class InstanceConfig
{
    public record ModuleResponse(string Key, string Name, bool IsCore, bool Enabled, IReadOnlyList<string> Requires);
    public record ProfileResponse(string Name, string ShortName, string? PrimaryColor, string? LogoUrl);
    public record ConfigResponse(ProfileResponse Profile, IReadOnlyList<ModuleResponse> Modules);

    public static void MapInstanceConfig(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/instance/config", GetAsync).AllowAnonymous();
    }

    private static async Task<IResult> GetAsync(HttpContext http, AbderaDbContext db, EnabledModules modules)
    {
        var profile = await SchoolProfile.GetCurrentAsync(db);
        var enabled = modules.Current;

        // Okul adı/renk/modül değişikliği bir sonraki sayfa açılışında hemen yansısın.
        http.Response.Headers.CacheControl = "no-cache";
        return Results.Ok(new ConfigResponse(
            ToProfileResponse(profile),
            ModuleCatalog.All
                .Select(module => new ModuleResponse(
                    module.Key, module.Name, module.IsCore, enabled.Contains(module.Key), module.Requires))
                .ToList()));
    }

    public static ProfileResponse ToProfileResponse(SchoolProfile profile) => new(
        profile.Name,
        profile.ShortName,
        profile.PrimaryColor,
        profile.LogoVersion is { } version ? $"/api/instance/logo?v={version:N}" : null);
}
