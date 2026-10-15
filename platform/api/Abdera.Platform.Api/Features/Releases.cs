using System.Security.Claims;
using System.Text.Json;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Api.Features;

// CI'ın ürettiği sürümler (.github/workflows/images.yml release.json) ve sürüme bağlı modül kataloğu (§16.8).
public static class Releases
{
    public record ModuleEntry(string Key, string Name, bool IsCore, IReadOnlyList<string> Requires);
    public record CreateRequest(string Version, string ApiImage, string WebImage, string? Revision, IReadOnlyList<ModuleEntry> Modules, string? Notes);
    public record ReleaseResponse(string Version, string ApiImage, string WebImage, string? Revision, string? Notes, bool IsWithdrawn, DateTimeOffset ReleasedAt, int SchoolCount);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapReleases(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/releases").RequireAuthorization(Auth.OperatorPolicy);
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapGet("/{version}/modules", ModulesAsync);
        group.MapPost("/{version}/withdraw", WithdrawAsync);
    }

    public static IReadOnlyList<ModuleEntry> Catalog(Release release) =>
        JsonSerializer.Deserialize<List<ModuleEntry>>(release.ModuleCatalogJson, Json) ?? [];

    private static async Task<IResult> ListAsync(PlatformDbContext db)
    {
        var counts = await db.Schools.Where(s => s.AppVersion != null && s.Status != SchoolStatus.Destroyed)
            .GroupBy(s => s.AppVersion!).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);
        var releases = await db.Releases.ToListAsync();
        return Results.Ok(releases
            .OrderByDescending(r => r.Version, Comparer<string>.Create(Release.Compare))
            .Select(r => new ReleaseResponse(r.Version, r.ApiImage, r.WebImage, r.Revision, r.Notes, r.IsWithdrawn, r.ReleasedAt, counts.GetValueOrDefault(r.Version))));
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, ClaimsPrincipal principal, PlatformDbContext db, TimeProvider time)
    {
        if (request.Modules is not { Count: > 0 } || request.Modules.Any(m => string.IsNullOrWhiteSpace(m.Key)))
            throw ValidationFailedException.For("modules", "Sürümün modül kataloğu gerekli.");
        var existing = await db.Releases.SingleOrDefaultAsync(r => r.Version == request.Version);
        if (existing is not null)
        {
            // Aynı sürüm aynı imajlarla yeniden kaydedilirse sorun yok; farklı imajla asla (digest değişmez).
            if (existing.ApiImage == request.ApiImage && existing.WebImage == request.WebImage) return Results.Ok();
            throw new ConflictException("Bu sürüm farklı imajlarla zaten kayıtlı.");
        }
        var release = Release.Create(request.Version, request.ApiImage, request.WebImage, request.Revision,
            JsonSerializer.Serialize(request.Modules, Json), request.Notes, time.GetUtcNow());
        db.Releases.Add(release);
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "release.registered", null, time.GetUtcNow(),
            afterJson: JsonSerializer.Serialize(new { release.Version, release.ApiImage, release.WebImage })));
        await db.SaveChangesAsync();
        return Results.Created($"/api/releases/{release.Version}", null);
    }

    private static async Task<IResult> ModulesAsync(string version, PlatformDbContext db)
    {
        var release = await db.Releases.SingleOrDefaultAsync(r => r.Version == version) ?? throw new NotFoundException("Sürüm bulunamadı.");
        return Results.Ok(Catalog(release));
    }

    private static async Task<IResult> WithdrawAsync(string version, ClaimsPrincipal principal, PlatformDbContext db, TimeProvider time)
    {
        var release = await db.Releases.SingleOrDefaultAsync(r => r.Version == version) ?? throw new NotFoundException("Sürüm bulunamadı.");
        release.Withdraw();
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "release.withdrawn", null, time.GetUtcNow(),
            afterJson: JsonSerializer.Serialize(new { release.Version })));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
