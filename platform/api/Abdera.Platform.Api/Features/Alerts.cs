using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Api.Features;

// Okullardan gelen alarmlar (okul tarafı: AlertWebhookNotifier). Gövde okulun kendi alarm sırrıyla
// HMAC-SHA256 imzalıdır; imzası tutmayan istek 401 alır. Okulu "instance" (kısaltma) alanından tanır.
public static class Alerts
{
    public const string SignatureHeader = "X-Abdera-Signature";
    public record AlertResponse(Guid Id, Guid SchoolId, string Slug, string Kind, string Level, string Detail, DateTimeOffset OccurredAt, DateTimeOffset ReceivedAt, DateTimeOffset? AcknowledgedAt);

    public static void MapAlerts(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/alerts", ReceiveAsync).AllowAnonymous().RequireRateLimiting("alerts");
        var group = app.MapGroup("/api/alerts").RequireAuthorization(Auth.OperatorPolicy);
        group.MapGet("", ListAsync);
        group.MapPost("/{id:guid}/ack", AcknowledgeAsync);
    }

    private static async Task<IResult> ReceiveAsync(HttpContext http, PlatformDbContext db, SecretVault vault, TimeProvider time)
    {
        using var reader = new StreamReader(http.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        if (body.Length > 16_384) return Results.StatusCode(413);
        JsonElement payload;
        try { payload = JsonDocument.Parse(body).RootElement; }
        catch (JsonException) { return Results.BadRequest(); }

        var slug = payload.TryGetProperty("instance", out var instance) ? instance.GetString() : null;
        var school = Slug.IsValid(slug) ? await db.Schools.SingleOrDefaultAsync(s => s.Slug == slug) : null;
        var secret = school is null ? null : await vault.GetAsync(school.Id, SecretNames.AlertsSecret);
        var expected = secret is null ? null : "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        var given = http.Request.Headers[SignatureHeader].ToString();
        if (expected is null || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(given)))
            return Results.Unauthorized();

        var occurredAt = payload.TryGetProperty("at", out var at) && at.TryGetDateTimeOffset(out var parsed) ? parsed : time.GetUtcNow();
        db.Alerts.Add(SchoolAlert.Create(school!.Id,
            payload.TryGetProperty("kind", out var kind) ? kind.GetString() ?? "unknown" : "unknown",
            payload.TryGetProperty("level", out var level) ? level.GetString() ?? "Unknown" : "Unknown",
            payload.TryGetProperty("detail", out var detail) ? detail.GetString() ?? "" : "",
            occurredAt, time.GetUtcNow()));
        await db.SaveChangesAsync();
        return Results.Accepted();
    }

    private static async Task<IResult> ListAsync(PlatformDbContext db, bool? open)
    {
        var query = db.Alerts.AsQueryable();
        if (open == true) query = query.Where(a => a.AcknowledgedAt == null);
        var alerts = await query.OrderByDescending(a => a.ReceivedAt).Take(200).ToListAsync();
        var slugs = await db.Schools.ToDictionaryAsync(s => s.Id, s => s.Slug);
        return Results.Ok(alerts.Select(a => new AlertResponse(a.Id, a.SchoolId, slugs.GetValueOrDefault(a.SchoolId) ?? "?", a.Kind, a.Level, a.Detail, a.OccurredAt, a.ReceivedAt, a.AcknowledgedAt)));
    }

    private static async Task<IResult> AcknowledgeAsync(Guid id, ClaimsPrincipal principal, PlatformDbContext db, TimeProvider time)
    {
        var alert = await db.Alerts.SingleOrDefaultAsync(a => a.Id == id) ?? throw new NotFoundException("Alarm bulunamadı.");
        alert.Acknowledge(time.GetUtcNow());
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "alert.acknowledged", alert.SchoolId, time.GetUtcNow()));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
