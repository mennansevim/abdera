using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Api.Features;

// Okulun kendi alan adı (ör. panel.okulum.com; okulun kendi web sitesi kendi yerinde kalır). Akış:
// 1) alan adı kaydedilir (bekleyen), panel müşteriye verilecek DNS kaydını gösterir;
// 2) "DNS'i kontrol et": alan adı okulun sunucusuna çözülüyorsa "Alan adını bağla" işlemi kurulur
//    (Caddy'ye eklenir, sertifikayı Caddy alır, https ile denenir, etkinleşir);
// 3) istenirse platform alt alanı yeni adrese yönlenir (/api ve /health hariç: platformun istekleri).
public static partial class SchoolDomains
{
    public record SetRequest(string Domain, bool Redirect);
    public record DomainResponse(string? CustomDomain, bool Redirect, bool Active, string Target, string? HostIp, string? LastCheck, DateTimeOffset? CheckedAt);
    public record CheckResponse(bool Ready, IReadOnlyList<string> Found, string? Expected, string Message);

    public static void MapSchoolDomains(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/schools/{id:guid}/domain").RequireAuthorization(Auth.OperatorPolicy);
        group.MapPut("", SetAsync);
        group.MapPost("/check", CheckAsync);
        group.MapDelete("", RemoveAsync);
    }

    public static async Task<DomainResponse> ResponseAsync(PlatformDbContext db, School school)
    {
        var hostIp = school.HostId is { } hostId ? await db.Hosts.Where(h => h.Id == hostId).Select(h => h.PublicIp).SingleOrDefaultAsync() : null;
        return new DomainResponse(school.CustomDomain, school.CustomDomainRedirect, school.CustomDomainActive, school.PrimaryDomain,
            hostIp, school.CustomDomainCheck, school.CustomDomainCheckedAt);
    }

    private static async Task<IResult> SetAsync(Guid id, SetRequest request, ClaimsPrincipal principal, PlatformDbContext db, IConfiguration config, TimeProvider time)
    {
        var school = await ActiveSchoolAsync(db, id);
        var domain = (request.Domain ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        if (domain.Length > 253 || !DomainPattern().IsMatch(domain))
            throw ValidationFailedException.For("domain", "Geçerli bir alan adı girilmeli (ör. panel.okulum.com).");
        if (domain == school.PrimaryDomain || config["Dns:BaseDomain"] is { Length: > 0 } baseDomain && (domain == baseDomain || domain.EndsWith("." + baseDomain, StringComparison.Ordinal)))
            throw ValidationFailedException.For("domain", "Platformun kendi alan adı kullanılamaz; okulun kendi alan adını girin.");
        if (await db.Schools.AnyAsync(s => s.Id != id && s.Status != SchoolStatus.Destroyed && (s.PrimaryDomain == domain || s.CustomDomain == domain)))
            throw new ConflictException("Bu alan adı başka bir okulda.");
        // Bağlı bir alan adı varken başkası yazılırsa önce eskisi kaldırılmalı; aynı alan adında yalnızca
        // yönlendirme tercihi değişiyorsa yeniden bağlanır.
        if (school.CustomDomainActive && school.CustomDomain != domain)
            throw new ConflictException("Okulun bağlı bir alan adı var; önce onu kaldırın.");
        await EnsureNoOpenRunAsync(db, id);
        var redirectChanged = school.CustomDomainActive && school.CustomDomainRedirect != request.Redirect;
        school.SetPendingDomain(domain, request.Redirect, time.GetUtcNow());
        if (redirectChanged) db.Runs.Add(ProvisioningRun.Create(id, RunKind.DomainAttach, time.GetUtcNow()));
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "school.domain_set", id, time.GetUtcNow(),
            afterJson: JsonSerializer.Serialize(new { domain, redirect = request.Redirect })));
        await db.SaveChangesAsync();
        return Results.Ok(await ResponseAsync(db, school));
    }

    private static async Task<IResult> CheckAsync(Guid id, ClaimsPrincipal principal, PlatformDbContext db, IDomainResolver resolver, TimeProvider time, CancellationToken cancellationToken)
    {
        var school = await ActiveSchoolAsync(db, id);
        if (school.CustomDomain is not { } domain) throw new ConflictException("Önce okulun alan adını kaydedin.");
        var hostIp = school.HostId is { } hostId ? await db.Hosts.Where(h => h.Id == hostId).Select(h => h.PublicIp).SingleOrDefaultAsync(cancellationToken) : null;
        var found = await resolver.ResolveAsync(domain, cancellationToken);
        var ready = hostIp is not null && found.Contains(hostIp);
        var message = ready
            ? $"{domain} sunucuya ({hostIp}) çözülüyor; alan adı bağlanıyor."
            : found.Count == 0
                ? $"{domain} için DNS kaydı bulunamadı. Kayıt yeni eklendiyse yayılması birkaç dakika sürebilir."
                : $"{domain} şu adreslere çözülüyor: {string.Join(", ", found)}; beklenen {hostIp}.";
        school.RecordDomainCheck(message, time.GetUtcNow());
        if (ready)
        {
            await EnsureNoOpenRunAsync(db, id);
            if (!school.CustomDomainActive) db.Runs.Add(ProvisioningRun.Create(id, RunKind.DomainAttach, time.GetUtcNow()));
        }
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new CheckResponse(ready, found, hostIp, message));
    }

    private static async Task<IResult> RemoveAsync(Guid id, ClaimsPrincipal principal, PlatformDbContext db, TimeProvider time)
    {
        var school = await db.Schools.SingleOrDefaultAsync(s => s.Id == id) ?? throw new NotFoundException("Okul bulunamadı.");
        if (school.CustomDomain is null) return Results.NoContent();
        await EnsureNoOpenRunAsync(db, id);
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "school.domain_remove_requested", id, time.GetUtcNow(),
            afterJson: JsonSerializer.Serialize(new { domain = school.CustomDomain })));
        if (school.CustomDomainActive)
        {
            // Bağlı alan adı Caddy'den kaldırılır; okul kaydı işlem bitince temizlenir.
            var run = ProvisioningRun.Create(id, RunKind.DomainDetach, time.GetUtcNow());
            db.Runs.Add(run);
            await db.SaveChangesAsync();
            return Results.Accepted($"/api/schools/{id}", new { runId = run.Id });
        }
        school.ClearDomain(time.GetUtcNow());
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<School> ActiveSchoolAsync(PlatformDbContext db, Guid id)
    {
        var school = await db.Schools.SingleOrDefaultAsync(s => s.Id == id) ?? throw new NotFoundException("Okul bulunamadı.");
        if (school.Status != SchoolStatus.Active) throw new ConflictException("Alan adı yalnızca etkin okulda bağlanabilir.");
        return school;
    }

    private static async Task EnsureNoOpenRunAsync(PlatformDbContext db, Guid schoolId)
    {
        if (await db.Runs.AnyAsync(r => r.SchoolId == schoolId && (r.Status == RunStatus.Pending || r.Status == RunStatus.Running)))
            throw new ConflictException("Bu okulda süren bir işlem var; bitmesini bekleyin.");
    }

    // Ajanın valid_domain kuralıyla aynı.
    [GeneratedRegex(@"^([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")]
    private static partial Regex DomainPattern();
}
