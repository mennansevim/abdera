using System.Security.Claims;
using System.Text.Json;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Api.Features;

// Gözlem paneli: bütün okulları ve sunucuları tek ekranda gösterir; log ve yedek listesi okulun sunucusundaki
// ajandan istek anında okunur. Sayılar telemetriden ve sunucu ölçümlerinden gelir (kişi verisi yok). Log
// metni kişisel veri içerebilir: platformda saklanmaz, her görüntüleme denetim kaydına yazılır.
public static class Monitoring
{
    public record OverviewResponse(
        DateTimeOffset GeneratedAt, FleetCounts Counts, FleetTotals Totals, IReadOnlyList<FleetHealth.Issue> Issues,
        IReadOnlyList<HostRow> Hosts, IReadOnlyList<SchoolRow> Schools, IReadOnlyList<Alerts.AlertResponse> RecentAlerts);
    public record FleetCounts(int Schools, int Active, int WithProblems, int Provisioning, int Suspended, int Hosts, int HostsWithProblems);
    public record FleetTotals(int Students, int StaffUsers, int Enrollments, long DatabaseBytes, int Errors24h, int Warnings24h, decimal YearlyTotal);
    public record HostRow(
        Guid Id, string Name, HostStatus Status, DateTimeOffset? TakenAt, bool Reachable, decimal? Load1, decimal? Load5, int? Cpus,
        int? MemTotalMb, int? MemAvailableMb, int? DiskTotalMb, int? DiskFreeMb, long? UptimeSeconds, int Schools, int Containers, int Restarts, string? Error);
    public record SchoolRow(
        Guid Id, string Slug, string DisplayName, SchoolStatus Status, string? HostName, string? AppVersion, DateTimeOffset? TakenAt,
        bool? Reachable, string? Health, int? Errors24h, int? Warnings24h, DateTimeOffset? LastErrorAt, int? DbConnections,
        int? DbWaitingOnLocks, long? DatabaseSizeBytes, string? LastMigration, DateTimeOffset? LastBackupAt, int? FailedNotifications,
        decimal? MemMb, int Restarts, LicenseState LicenseState, string Severity);
    public record HostStatsResponse(HostRow Host, IReadOnlyList<HostPoint> Points, IReadOnlyList<ContainerStat> Containers);
    public record HostPoint(DateTimeOffset TakenAt, bool Reachable, decimal? Load1, int? MemUsedMb, int? DiskUsedMb);
    public record BackupItem(string Name, string Location, long Size, DateTimeOffset ModifiedAt);
    public record BackupsResponse(IReadOnlyList<BackupItem> Items, DateTimeOffset? LastSucceededAt);

    public static void MapMonitoring(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/overview", OverviewAsync).RequireAuthorization(Auth.OperatorPolicy);
        app.MapGet("/api/hosts/{id:guid}/stats", HostStatsAsync).RequireAuthorization(Auth.OperatorPolicy);
        app.MapGet("/api/schools/{id:guid}/logs", LogsAsync).RequireAuthorization(Auth.OperatorPolicy);
        app.MapGet("/api/schools/{id:guid}/backups", BackupsAsync).RequireAuthorization(Auth.OperatorPolicy);
    }

    private static async Task<IResult> OverviewAsync(PlatformDbContext db, IConfiguration config, TimeProvider time)
    {
        var now = time.GetUtcNow();
        var today = PlatformClock.Today(config, time);
        var schools = await db.Schools.Where(s => s.Status != SchoolStatus.Destroyed).OrderBy(s => s.DisplayName).ToListAsync();
        var hosts = await db.Hosts.OrderBy(h => h.Name).ToListAsync();
        var ids = schools.Select(s => s.Id).ToList();
        var since = now.AddHours(-2);
        var telemetry = (await db.Telemetry.Where(t => ids.Contains(t.SchoolId) && t.TakenAt >= since).ToListAsync())
            .GroupBy(t => t.SchoolId).ToDictionary(g => g.Key, g => g.MaxBy(t => t.TakenAt)!);
        var hostIds = hosts.Select(h => h.Id).ToList();
        var hostStats = (await db.HostSnapshots.Where(h => hostIds.Contains(h.HostId) && h.TakenAt >= since).ToListAsync())
            .GroupBy(h => h.HostId).ToDictionary(g => g.Key, g => g.MaxBy(h => h.TakenAt)!);
        var containers = hostStats.Values.Where(h => h.Reachable).SelectMany(h => h.Containers).ToList();

        var issues = new List<FleetHealth.Issue>();
        var rows = new List<SchoolRow>();
        foreach (var school in schools)
        {
            var latest = telemetry.GetValueOrDefault(school.Id);
            var own = containers.Where(c => c.School == school.Slug).ToList();
            var schoolIssues = FleetHealth.ForSchool(school, latest, own, today, now).ToList();
            issues.AddRange(schoolIssues);
            rows.Add(new SchoolRow(
                school.Id, school.Slug, school.DisplayName, school.Status, hosts.FirstOrDefault(h => h.Id == school.HostId)?.Name,
                school.AppVersion, latest?.TakenAt, latest?.Reachable, latest?.Health, latest?.Errors24h, latest?.Warnings24h,
                latest?.LastErrorAt, latest?.DbConnections, latest?.DbWaitingOnLocks, latest?.DatabaseSizeBytes, latest?.LastMigration,
                latest?.LastBackupAt, latest?.FailedNotifications,
                own.Count == 0 ? null : own.Sum(c => c.MemMb ?? 0), own.Sum(c => c.Restarts),
                SchoolPackage.StateOn(school.LicenseEndsOn, today), Severity(schoolIssues)));
        }

        var hostRows = new List<HostRow>();
        var hostsWithProblems = 0;
        foreach (var host in hosts)
        {
            var latest = hostStats.GetValueOrDefault(host.Id);
            var hostIssues = FleetHealth.ForHost(host, latest, now).ToList();
            issues.AddRange(hostIssues);
            if (hostIssues.Count > 0) hostsWithProblems++;
            hostRows.Add(ToHostRow(host, latest, schools.Count(s => s.HostId == host.Id)));
        }

        var alerts = await db.Alerts.Where(a => a.AcknowledgedAt == null).OrderByDescending(a => a.ReceivedAt).Take(8).ToListAsync();
        var slugs = schools.ToDictionary(s => s.Id, s => s.Slug);
        var fresh = telemetry.Values.Where(t => t.Reachable).ToList();
        return Results.Ok(new OverviewResponse(
            now,
            new FleetCounts(schools.Count, schools.Count(s => s.Status == SchoolStatus.Active), rows.Count(r => r.Severity != "ok"),
                schools.Count(s => s.Status == SchoolStatus.Provisioning), schools.Count(s => s.Status == SchoolStatus.Suspended),
                hosts.Count, hostsWithProblems),
            new FleetTotals(
                fresh.Sum(t => t.ActiveStudents ?? 0), fresh.Sum(t => t.ActiveStaffUsers ?? 0), fresh.Sum(t => t.ActiveEnrollments ?? 0),
                fresh.Sum(t => t.DatabaseSizeBytes ?? 0), fresh.Sum(t => t.Errors24h ?? 0), fresh.Sum(t => t.Warnings24h ?? 0),
                schools.Where(s => s.Status != SchoolStatus.PendingDestroy).Sum(s => s.YearlyTotal)),
            issues.OrderByDescending(i => i.Severity).ThenBy(i => i.Subject, StringComparer.CurrentCulture).ToList(),
            hostRows, rows,
            alerts.Select(a => new Alerts.AlertResponse(a.Id, a.SchoolId, slugs.GetValueOrDefault(a.SchoolId, "?"), a.Kind, a.Level, a.Detail,
                a.OccurredAt, a.ReceivedAt, a.AcknowledgedAt)).ToList()));
    }

    private static string Severity(IReadOnlyCollection<FleetHealth.Issue> issues) =>
        issues.Any(i => i.Severity == FleetHealth.Level.Critical) ? "critical" : issues.Count > 0 ? "warning" : "ok";

    private static HostRow ToHostRow(ServerHost host, HostSnapshot? latest, int schools) => new(
        host.Id, host.Name, host.Status, latest?.TakenAt, latest?.Reachable ?? false, latest?.Load1, latest?.Load5, latest?.Cpus,
        latest?.MemTotalMb, latest?.MemAvailableMb, latest?.DiskTotalMb, latest?.DiskFreeMb, latest?.UptimeSeconds, schools,
        latest?.Containers.Count ?? 0, latest?.Containers.Sum(c => c.Restarts) ?? 0, latest?.Error);

    // Sunucunun ölçüm eğrisi (en çok 7 gün) ve son ölçümdeki konteynerler.
    private static async Task<IResult> HostStatsAsync(Guid id, int? hours, PlatformDbContext db, TimeProvider time)
    {
        var host = await db.Hosts.SingleOrDefaultAsync(h => h.Id == id) ?? throw new NotFoundException("Sunucu bulunamadı.");
        var since = time.GetUtcNow().AddHours(-Math.Clamp(hours ?? 24, 1, 24 * 7));
        var points = await db.HostSnapshots.Where(s => s.HostId == id && s.TakenAt >= since).OrderBy(s => s.TakenAt).ToListAsync();
        var latest = points.LastOrDefault();
        var schools = await db.Schools.CountAsync(s => s.HostId == id && s.Status != SchoolStatus.Destroyed);
        return Results.Ok(new HostStatsResponse(
            ToHostRow(host, latest, schools),
            points.Select(p => new HostPoint(p.TakenAt, p.Reachable, p.Load1,
                p.MemTotalMb - p.MemAvailableMb, p.DiskTotalMb - p.DiskFreeMb)).ToList(),
            latest?.Containers ?? []));
    }

    private static readonly HashSet<string> LogServices = ["api", "web", "db", "backup-gw"];
    private static readonly HashSet<string> LogWindows = ["15m", "1h", "6h", "24h"];

    // Okulun konteyner logu, istek anında ajandan. Saklanmaz; kim, hangi okulun hangi servisine baktı audit'e yazılır.
    private static async Task<IResult> LogsAsync(
        Guid id, string? service, string? since, int? tail, ClaimsPrincipal principal, PlatformDbContext db, IHostAgent agent, TimeProvider time,
        CancellationToken cancellationToken)
    {
        service ??= "api";
        since ??= "1h";
        var lines = Math.Clamp(tail ?? 500, 1, 2000);
        if (!LogServices.Contains(service)) throw ValidationFailedException.For("service", "Servis api, web, db ya da backup-gw olmalı.");
        if (!LogWindows.Contains(since)) throw ValidationFailedException.For("since", "Süre 15m, 1h, 6h ya da 24h olmalı.");
        var (school, host) = await SchoolOnHostAsync(db, id);
        db.AuditLogs.Add(PlatformAuditLog.Record(Auth.UserId(principal), "school.logs_viewed", id, time.GetUtcNow(),
            afterJson: JsonSerializer.Serialize(new { service, since, tail = lines })));
        await db.SaveChangesAsync(cancellationToken);
        var result = await RunAsync(agent, host, AgentCommand.For("logs", school.Slug, [service, since, lines.ToString()]), cancellationToken);
        return result.Ok
            ? Results.Text(result.Output, "text/plain; charset=utf-8")
            : Results.Problem(title: "Log okunamadı", detail: result.Describe(), statusCode: 502);
    }

    // R2'deki yedekler (uygulamanın günlük yedeği ve askı/silme öncesi dökümler) ve sunucudaki dökümler.
    private static async Task<IResult> BackupsAsync(Guid id, PlatformDbContext db, IHostAgent agent, CancellationToken cancellationToken)
    {
        var (school, host) = await SchoolOnHostAsync(db, id);
        var result = await RunAsync(agent, host, AgentCommand.For("backups", school.Slug), cancellationToken);
        if (!result.Ok) return Results.Problem(title: "Yedek listesi alınamadı", detail: result.Describe(), statusCode: 502);
        var lastSucceeded = await db.Telemetry.Where(t => t.SchoolId == id && t.LastBackupAt != null)
            .OrderByDescending(t => t.TakenAt).Select(t => t.LastBackupAt).FirstOrDefaultAsync(cancellationToken);
        return Results.Ok(new BackupsResponse(ParseBackups(result.Output), lastSucceeded));
    }

    public static IReadOnlyList<BackupItem> ParseBackups(string agentOutput)
    {
        var root = JsonDocument.Parse(agentOutput).RootElement;
        var items = new List<BackupItem>();
        foreach (var remote in root.GetProperty("remote").EnumerateArray())
        {
            var path = remote.GetProperty("Path").GetString() ?? "";
            items.Add(new BackupItem(path, path.StartsWith("suspended/", StringComparison.Ordinal) ? "R2 (saklama dışı)" : "R2",
                remote.GetProperty("Size").GetInt64(), remote.GetProperty("ModTime").GetDateTimeOffset()));
        }
        foreach (var local in root.GetProperty("local").EnumerateArray())
            items.Add(new BackupItem(local.GetProperty("name").GetString() ?? "", "Sunucu", local.GetProperty("size").GetInt64(),
                DateTimeOffset.FromUnixTimeSeconds(local.GetProperty("modTime").GetInt64())));
        return items.OrderByDescending(i => i.ModifiedAt).ToList();
    }

    private static async Task<(School School, ServerHost Host)> SchoolOnHostAsync(PlatformDbContext db, Guid id)
    {
        var school = await db.Schools.SingleOrDefaultAsync(s => s.Id == id) ?? throw new NotFoundException("Okul bulunamadı.");
        if (school.HostId is not { } hostId || school.Status == SchoolStatus.Destroyed)
            throw new ConflictException("Okul bir sunucuda kurulu değil.");
        var host = await db.Hosts.SingleAsync(h => h.Id == hostId);
        return (school, host);
    }

    private static async Task<AgentResult> RunAsync(IHostAgent agent, ServerHost host, AgentCommand command, CancellationToken cancellationToken)
    {
        try { return await agent.RunAsync(host, command, cancellationToken); }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested) { return new AgentResult(-1, "", ex.Message); }
    }
}
