using System.Security.Claims;
using System.Text.Json;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Api.Features;

// Okul sunucuları (§4.4): ekle, kapasiteyi gör, ajanı yokla.
public static class Hosts
{
    public record CreateRequest(string Name, string Address, int SshPort, string SshUser, string HostKeyFingerprint, string PublicIp, int TotalMemoryMb);
    public record HostResponse(
        Guid Id, string Name, string Address, int SshPort, string SshUser, string HostKeyFingerprint, string PublicIp,
        int TotalMemoryMb, int ReservedMemoryMb, int CapacityMb, HostStatus Status, int SchoolCount, string? AgentVersion, DateTimeOffset? LastCheckedAt);
    public record StatusRequest(HostStatus Status);

    public static void MapHosts(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/hosts").RequireAuthorization(Auth.OperatorPolicy);
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapPost("/{id:guid}/check", CheckAsync);
        group.MapPut("/{id:guid}/status", SetStatusAsync);
    }

    private static async Task<IResult> ListAsync(PlatformDbContext db)
    {
        var counts = await db.Schools.Where(s => s.HostId != null && s.Status != SchoolStatus.Destroyed)
            .GroupBy(s => s.HostId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count);
        var hosts = await db.Hosts.OrderBy(h => h.Name).ToListAsync();
        return Results.Ok(hosts.Select(h => ToResponse(h, counts.GetValueOrDefault(h.Id))));
    }

    private static async Task<IResult> CreateAsync(CreateRequest request, ClaimsPrincipal principal, PlatformDbContext db, TimeProvider time)
    {
        if (await db.Hosts.AnyAsync(h => h.Name == request.Name.Trim())) throw new ConflictException("Bu adla bir sunucu var.");
        var host = ServerHost.Create(request.Name, request.Address, request.SshPort, request.SshUser, request.HostKeyFingerprint,
            request.PublicIp, request.TotalMemoryMb, time.GetUtcNow());
        db.Hosts.Add(host);
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "host.created", null, time.GetUtcNow(),
            afterJson: JsonSerializer.Serialize(new { host.Name, host.Address, host.PublicIp, host.TotalMemoryMb })));
        await db.SaveChangesAsync();
        return Results.Created($"/api/hosts/{host.Id}", ToResponse(host, 0));
    }

    // Ajanı yoklar: bağlantı, parmak izi ve ajan sürümü doğrulanır.
    private static async Task<IResult> CheckAsync(Guid id, PlatformDbContext db, IHostAgent agent, TimeProvider time, CancellationToken cancellationToken)
    {
        var host = await db.Hosts.SingleOrDefaultAsync(h => h.Id == id) ?? throw new NotFoundException("Sunucu bulunamadı.");
        AgentResult result;
        try { result = await agent.RunAsync(host, AgentCommand.For("host-info"), cancellationToken); }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Problem(title: "Sunucuya ulaşılamadı", detail: ex.Message, statusCode: 502);
        }
        if (!result.Ok) return Results.Problem(title: "Ajan hata verdi", detail: result.Describe(), statusCode: 502);
        var info = JsonDocument.Parse(result.Output).RootElement;
        host.RecordCheck(info.GetProperty("agent").GetString() ?? "?", time.GetUtcNow());
        await db.SaveChangesAsync();
        return Results.Ok(info);
    }

    private static async Task<IResult> SetStatusAsync(Guid id, StatusRequest request, ClaimsPrincipal principal, PlatformDbContext db, TimeProvider time)
    {
        var host = await db.Hosts.SingleOrDefaultAsync(h => h.Id == id) ?? throw new NotFoundException("Sunucu bulunamadı.");
        var before = host.Status;
        host.SetStatus(request.Status, time.GetUtcNow());
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "host.status_changed", null, time.GetUtcNow(),
            beforeJson: JsonSerializer.Serialize(new { status = before.ToString() }),
            afterJson: JsonSerializer.Serialize(new { host.Name, status = request.Status.ToString() })));
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static HostResponse ToResponse(ServerHost h, int schools) => new(
        h.Id, h.Name, h.Address, h.SshPort, h.SshUser, h.HostKeyFingerprint, h.PublicIp,
        h.TotalMemoryMb, h.ReservedMemoryMb, h.CapacityMb, h.Status, schools, h.AgentVersion, h.LastCheckedAt);
}
