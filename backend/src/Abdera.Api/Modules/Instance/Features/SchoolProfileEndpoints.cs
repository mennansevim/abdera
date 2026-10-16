using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Instance.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Instance.Features;

// Okulun adı, kısa adı, rengi ve logosu. Yalnızca yönetici değiştirir; logo herkese açık
// sunulur çünkü giriş ekranında (oturum açılmadan) da görünür.
public static class SchoolProfileEndpoints
{
    // LegalName/ContactEmail verilmezse (eski istemci) dokunulmaz; boş metin alanı temizler.
    public record UpdateRequest(string Name, string ShortName, string? PrimaryColor, string? LegalName = null, string? ContactEmail = null);

    public static void MapSchoolProfile(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/instance/logo", GetLogoAsync).AllowAnonymous();

        var admin = app.MapGroup("/api/instance").RequireAuthorization(AuthorizationPolicies.AdminOnly);
        admin.MapPut("/profile", UpdateAsync);
        admin.MapPut("/logo", UploadLogoAsync).DisableAntiforgery();
        admin.MapDelete("/logo", DeleteLogoAsync);
    }

    private static async Task<IResult> GetLogoAsync(HttpContext http, AbderaDbContext db)
    {
        var profile = await db.SchoolProfiles.AsNoTracking()
            .Where(p => p.Id == SchoolProfile.SingletonId && p.Logo != null)
            .Select(p => new { p.Logo, p.LogoContentType, p.LogoVersion })
            .SingleOrDefaultAsync();
        if (profile is null) return Results.NotFound();

        var etag = $"\"{profile.LogoVersion:N}\"";
        if (http.Request.Headers.IfNoneMatch.ToString() == etag)
            return Results.StatusCode(StatusCodes.Status304NotModified);

        http.Response.Headers.ETag = etag;
        // URL logo sürümünü taşıdığı (?v=) için uzun önbellek güvenli; logo değişince URL değişir.
        http.Response.Headers.CacheControl = "public, max-age=86400";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return Results.File(profile.Logo!, profile.LogoContentType!);
    }

    private static async Task<IResult> UpdateAsync(
        UpdateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock, IConfiguration config)
    {
        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var profile = await LoadOrCreateAsync(db, now, config);
        var before = JsonSerializer.Serialize(new { profile.Name, profile.ShortName, profile.PrimaryColor, profile.LegalName, profile.ContactEmail });

        profile.UpdateIdentity(request.Name, request.ShortName, request.PrimaryColor, actorId, now);
        if (request.LegalName is not null || request.ContactEmail is not null)
            profile.UpdateContact(request.LegalName ?? profile.LegalName, request.ContactEmail ?? profile.ContactEmail, actorId, now);

        db.AuditLogs.Add(AuditLog.Record(
            actorId, "school_profile.updated", nameof(SchoolProfile), profile.Id, now,
            beforeJson: before,
            afterJson: JsonSerializer.Serialize(new { profile.Name, profile.ShortName, profile.PrimaryColor, profile.LegalName, profile.ContactEmail })));
        await db.SaveChangesAsync();

        return Results.Ok(InstanceConfig.ToProfileResponse(profile));
    }

    private static async Task<IResult> UploadLogoAsync(
        IFormFile? file, ClaimsPrincipal principal, AbderaDbContext db, IClock clock, IConfiguration config)
    {
        // Boyut sınırı belleğe almadan ÖNCE kontrol edilir (StudentPhotos ile aynı gerekçe).
        if (file is null || file.Length == 0 || file.Length > SchoolProfile.MaxLogoBytes)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["file"] = ["Logo PNG, JPEG veya WebP olmalı ve en fazla 512 KB olabilir."],
            });

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        var profile = await LoadOrCreateAsync(db, now, config);
        profile.ReplaceLogo(buffer.ToArray(), actorId, now);

        db.AuditLogs.Add(AuditLog.Record(
            actorId, "school_profile.logo_replaced", nameof(SchoolProfile), profile.Id, now,
            afterJson: JsonSerializer.Serialize(new { contentType = profile.LogoContentType, sizeBytes = profile.Logo!.Length })));
        await db.SaveChangesAsync();

        return Results.Ok(InstanceConfig.ToProfileResponse(profile));
    }

    private static async Task<IResult> DeleteLogoAsync(ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var profile = await db.SchoolProfiles.SingleOrDefaultAsync(p => p.Id == SchoolProfile.SingletonId);
        if (profile is null || !profile.HasLogo) return Results.NoContent();

        var now = clock.UtcNow;
        var actorId = AuthContext.GetUserId(principal);
        profile.RemoveLogo(actorId, now);
        db.AuditLogs.Add(AuditLog.Record(actorId, "school_profile.logo_removed", nameof(SchoolProfile), profile.Id, now));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<SchoolProfile> LoadOrCreateAsync(AbderaDbContext db, DateTimeOffset now, IConfiguration config)
    {
        var profile = await db.SchoolProfiles.SingleOrDefaultAsync(p => p.Id == SchoolProfile.SingletonId);
        if (profile is not null) return profile;

        profile = SchoolProfile.CreateDefault(now, config);
        db.SchoolProfiles.Add(profile);
        return profile;
    }
}
