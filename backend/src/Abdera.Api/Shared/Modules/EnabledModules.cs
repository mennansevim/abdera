namespace Abdera.Api.Shared.Modules;

// Modules:Enabled her sorguda IConfiguration'dan okunur (CLAUDE.md "Program.cs'te konfigürasyon
// okuma kuralı"): WebApplicationFactory'nin test-time overlay'i de görülür, testler modülleri
// açıp kapatabilir. Değer küçük bir virgüllü liste; her istekte ayrıştırmanın maliyeti önemsiz.
public sealed class EnabledModules(IConfiguration configuration)
{
    public const string ConfigurationKey = "Modules:Enabled";

    public IReadOnlySet<string> Current => ModuleCatalog.ResolveEnabled(configuration[ConfigurationKey]);

    public bool IsEnabled(string key) => Current.Contains(key);
}

// Bir uç noktayı (veya bir MapGroup'u) modüle bağlar. ModuleGateMiddleware bu işareti okur.
public sealed record RequiresModuleMetadata(string Key);

public static class ModuleEndpointExtensions
{
    public static TBuilder RequireModule<TBuilder>(this TBuilder builder, string key)
        where TBuilder : IEndpointConventionBuilder
    {
        if (ModuleCatalog.Find(key) is null)
        {
            throw new ArgumentException($"Katalogda olmayan modül: {key}", nameof(key));
        }

        builder.WithMetadata(new RequiresModuleMetadata(key));
        return builder;
    }

    // Modülün bütün uçlarını tek bir boş önekli grupta toplar: modül dosyasındaki Map* çağrıları
    // değişmeden (hepsi IEndpointRouteBuilder alıyor) grubun işaretini devralır.
    public static RouteGroupBuilder MapModule(this IEndpointRouteBuilder app, string key) =>
        app.MapGroup(string.Empty).RequireModule(key);
}

// Kimlik doğrulamadan ÖNCE çalışır: kapalı modülün uçları oturumlu/oturumsuz herkese aynı 404'ü
// döner, modülün varlığı 401/403 farkından anlaşılmaz.
public sealed class ModuleGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, EnabledModules modules)
    {
        var required = context.GetEndpoint()?.Metadata.GetOrderedMetadata<RequiresModuleMetadata>();
        if (required is { Count: > 0 } && required.Any(metadata => !modules.IsEnabled(metadata.Key)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                title = "Modül kapalı",
                status = StatusCodes.Status404NotFound,
                detail = "Bu modül bu okulda açık değil.",
            });
            return;
        }

        await next(context);
    }
}
