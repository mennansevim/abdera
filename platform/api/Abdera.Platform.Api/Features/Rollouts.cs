using System.Security.Claims;
using System.Text.Json;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Persistence;
using Abdera.Platform.Api.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Api.Features;

// Filo güncellemesi başlat/izle/durdur (§7). Yürütme RolloutWorker'da.
public static class Rollouts
{
    public record StartRequest(string Version);
    public record WaveResponse(int Wave, int Schools, int Done);
    public record RolloutResponse(Guid Id, string Version, int Wave, RolloutStatus Status, string? HaltReason, DateTimeOffset StartedAt,
        DateTimeOffset? WaveCompletedAt, DateTimeOffset? FinishedAt, IReadOnlyList<WaveResponse> Waves);

    public static void MapRollouts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rollouts").RequireAuthorization(Auth.OperatorPolicy);
        group.MapGet("", ListAsync);
        group.MapPost("", StartAsync);
        group.MapPost("/{id:guid}/halt", HaltAsync);
    }

    private static async Task<IResult> ListAsync(PlatformDbContext db)
    {
        var eligible = await db.Schools.Where(s => s.Status == SchoolStatus.Active && !s.IsVersionPinned).ToListAsync();
        var rollouts = await db.Rollouts.OrderByDescending(r => r.StartedAt).Take(20).ToListAsync();
        return Results.Ok(rollouts.Select(r => new RolloutResponse(r.Id, r.Version, r.Wave, r.Status, r.HaltReason, r.StartedAt, r.WaveCompletedAt, r.FinishedAt,
            Enumerable.Range(0, 3).Select(wave =>
            {
                var members = RolloutWorker.WaveMembers(eligible, wave);
                return new WaveResponse(wave, members.Count, members.Count(s => s.AppVersion == r.Version));
            }).ToList())));
    }

    private static async Task<IResult> StartAsync(StartRequest request, ClaimsPrincipal principal, PlatformDbContext db, TimeProvider time)
    {
        var release = await db.Releases.SingleOrDefaultAsync(r => r.Version == request.Version && !r.IsWithdrawn)
            ?? throw ValidationFailedException.For("version", "Sürüm bulunamadı ya da geri çekilmiş.");
        if (await db.Rollouts.AnyAsync(r => r.Status == RolloutStatus.Running || r.Status == RolloutStatus.Paused))
            throw new ConflictException("Süren bir dağıtım var.");
        var rollout = Rollout.Start(release.Version, time.GetUtcNow());
        db.Rollouts.Add(rollout);
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "rollout.started", null, time.GetUtcNow(),
            afterJson: JsonSerializer.Serialize(new { rollout.Id, release.Version })));
        await db.SaveChangesAsync();
        return Results.Created($"/api/rollouts/{rollout.Id}", new { rollout.Id });
    }

    private static async Task<IResult> HaltAsync(Guid id, ClaimsPrincipal principal, PlatformDbContext db, TimeProvider time)
    {
        var rollout = await db.Rollouts.SingleOrDefaultAsync(r => r.Id == id) ?? throw new NotFoundException("Dağıtım bulunamadı.");
        if (rollout.Status != RolloutStatus.Running) throw new ConflictException("Dağıtım çalışmıyor.");
        rollout.Halt("Yönetici durdurdu.", time.GetUtcNow());
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "rollout.halted", null, time.GetUtcNow(),
            afterJson: JsonSerializer.Serialize(new { rollout.Id, rollout.Version, rollout.Wave })));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
