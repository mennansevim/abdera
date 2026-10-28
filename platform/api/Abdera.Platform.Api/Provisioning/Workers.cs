using System.Text.Json;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Features;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Api.Provisioning;

// Bekleyen işlemleri FOR UPDATE SKIP LOCKED ile alır ve adımları yürütür (§3.3, §6). Sahiplik
// süreli (lease): platform süreci ölürse süre dolunca işlem başka bir tura devredilir. Her adımdan
// sonra kayıt xmin ile yazılır; işlemi başka bir tur devraldıysa eski tur DbUpdateConcurrencyException
// alır ve durur (geç kalan eski çalışanın komutları kaydı ezmez).
public class ProvisioningWorker(IServiceScopeFactory scopes, IConfiguration config, TimeProvider time, ILogger<ProvisioningWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(config.GetValue("Provisioning:PollSeconds", 3));
        using var timer = new PeriodicTimer(interval, time);
        do
        {
            try
            {
                while (await RunNextAsync(stoppingToken)) { }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Kurulum turu başarısız oldu.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // Bir işlemi baştan sona (ya da ilk hataya kadar) yürütür. İşlem yoksa false.
    public async Task<bool> RunNextAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<ProvisioningEngine>();
        var now = time.GetUtcNow();

        ProvisioningRun? run;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            // xmin bir sistem kolonu: SELECT * onu getirmez, eşzamanlılık belirteci için açıkça seçilir.
            run = await db.Runs.FromSqlInterpolated($@"
                SELECT *, xmin FROM provisioning_runs
                WHERE status = 'Pending' OR (status = 'Running' AND lease_until < {now})
                ORDER BY created_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(cancellationToken);
            if (run is null) return false;
            run.Claim(now, Lease);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var school = await db.Schools.SingleAsync(s => s.Id == run.SchoolId, cancellationToken);
        var plan = engine.PlanFor(run.Kind);
        try
        {
            for (var step = run.CurrentStep; step < plan.Count; step++)
            {
                // Önceki sahip bu adımın ortasında öldüyse (kayıt hâlâ Running) aynı deneme devralınır.
                var record = await db.Steps
                    .Where(s => s.RunId == run.Id && s.Step == step && s.Status == StepStatus.Running)
                    .OrderByDescending(s => s.Attempt).FirstOrDefaultAsync(cancellationToken);
                if (record is null)
                {
                    var attempt = await db.Steps.CountAsync(s => s.RunId == run.Id && s.Step == step, cancellationToken) + 1;
                    record = ProvisioningStep.Start(run.Id, step, plan[step].Name, attempt, time.GetUtcNow());
                    db.Steps.Add(record);
                    await db.SaveChangesAsync(cancellationToken);
                }

                StepOutcome outcome;
                try
                {
                    outcome = await plan[step].Execute(new StepContext(run, school, step, record.Attempt, cancellationToken));
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "{Kind} {Slug}: '{Step}' adımı hata verdi.", run.Kind, school.Slug, plan[step].Name);
                    outcome = StepOutcome.Fail(ex.Message);
                }

                record.Finish(outcome.Success, outcome.Log, time.GetUtcNow());
                if (!outcome.Success)
                {
                    run.Fail($"{plan[step].Name}: {outcome.Log}", time.GetUtcNow());
                    if (run.Kind == RunKind.Create) school.MarkFailed(time.GetUtcNow());
                    // Başarısız güncellemede ajan önceki imaja döner; okul hâlâ eski sürümde. Hedef temizlenir ki panel
                    // okulu bozuk sürüme geçiyormuş gibi göstermesin (işlemin kendi hedefi "Yeniden dene" için kalır).
                    if (run.Kind == RunKind.Upgrade) school.ClearTargetVersion(time.GetUtcNow());
                    db.AuditLogs.Add(PlatformAuditLog.Record(null, "run.failed", school.Id, time.GetUtcNow(),
                        afterJson: JsonSerializer.Serialize(new { run.Id, kind = run.Kind.ToString(), step = plan[step].Name, error = outcome.Log })));
                    await db.SaveChangesAsync(cancellationToken);
                    return true;
                }

                run.Advance(step + 1, time.GetUtcNow(), Lease);
                await db.SaveChangesAsync(cancellationToken);
            }

            run.Succeed(time.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("{Kind} {Slug}: tamamlandı.", run.Kind, school.Slug);
        }
        catch (DbUpdateConcurrencyException)
        {
            // İşlemi başka bir tur devraldı (bu turun sahipliği düştü); yazılan hiçbir şey kalmaz.
            logger.LogWarning("{Kind} {Slug}: işlem başka bir tura geçti, bu tur durdu.", run.Kind, school.Slug);
        }
        return true;
    }
}

// Filo güncellemesi (§7): kanarya → ~%10 → kalan. Bir dalgada hata olursa dağıtım durur.
public class RolloutWorker(IServiceScopeFactory scopes, IConfiguration config, TimeProvider time, ILogger<RolloutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(config.GetValue("Rollouts:PollSeconds", 30)), time);
        do
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Dağıtım turu başarısız oldu."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public static IReadOnlyList<School> WaveMembers(IReadOnlyList<School> eligible, int wave)
    {
        var canaries = eligible.Where(s => s.IsCanary).OrderBy(s => s.Slug, StringComparer.Ordinal).ToList();
        var rest = eligible.Where(s => !s.IsCanary).OrderBy(s => s.Slug, StringComparer.Ordinal).ToList();
        var firstWaveSize = rest.Count == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(rest.Count * 0.1));
        return wave switch
        {
            0 => canaries,
            1 => rest.Take(firstWaveSize).ToList(),
            _ => rest.Skip(firstWaveSize).ToList(),
        };
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var rollout = await db.Rollouts.SingleOrDefaultAsync(r => r.Status == RolloutStatus.Running, cancellationToken);
        if (rollout is null) return;
        var now = time.GetUtcNow();

        var runs = await db.Runs.Where(r => r.RolloutId == rollout.Id).ToListAsync(cancellationToken);
        if (runs.FirstOrDefault(r => r.Status == RunStatus.Failed) is { } failed)
        {
            var slug = await db.Schools.Where(s => s.Id == failed.SchoolId).Select(s => s.Slug).SingleAsync(cancellationToken);
            rollout.Halt($"{slug}: {failed.Error}", now);
            db.AuditLogs.Add(PlatformAuditLog.Record(null, "rollout.halted", failed.SchoolId, now,
                afterJson: JsonSerializer.Serialize(new { rollout.Version, rollout.Wave, slug, failed.Error })));
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Kurulmuş, sürümü sabitlenmemiş, etkin okullar. Askıdaki okul güncellenmez.
        var eligible = await db.Schools
            .Where(s => s.Status == SchoolStatus.Active && !s.IsVersionPinned)
            .ToListAsync(cancellationToken);
        var members = WaveMembers(eligible, rollout.Wave);
        var pending = members.Where(s => s.AppVersion != rollout.Version).ToList();
        var open = runs.Where(r => r.IsOpen).Select(r => r.SchoolId).ToHashSet();

        if (pending.Count == 0 && open.Count == 0)
        {
            rollout.CompleteWave(now);
            var soak = TimeSpan.FromMinutes(config.GetValue("Rollouts:CanarySoakMinutes", 24 * 60));
            if (rollout.Wave == 0 && members.Count > 0 && now - rollout.WaveCompletedAt < soak)
            {
                await db.SaveChangesAsync(cancellationToken);
                return; // kanarya bekleme süresi (§7: 24 saat sorunsuz)
            }
            if (rollout.Wave >= 2) rollout.Complete(now);
            else rollout.NextWave(now);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        // Dalga içinde aynı anda en fazla N okul güncellenir.
        var parallel = config.GetValue("Rollouts:Parallel", 3);
        foreach (var school in pending.Where(s => !open.Contains(s.Id)).Take(Math.Max(0, parallel - open.Count)))
        {
            if (await db.Runs.AnyAsync(r => r.SchoolId == school.Id && (r.Status == RunStatus.Pending || r.Status == RunStatus.Running), cancellationToken))
                continue; // okulda başka bir işlem sürüyor; sonraki turda
            school.SetTargetVersion(rollout.Version, now);
            db.Runs.Add(ProvisioningRun.Create(school.Id, RunKind.Upgrade, now, rollout.Version, rollout.Id));
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}

// Okulların telemetrisini toplar (§3.3). Ulaşılamayan okul kutucukta kırmızıya döner.
public class TelemetryPoller(IServiceScopeFactory scopes, IConfiguration config, TimeProvider time, ILogger<TelemetryPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(config.GetValue("Telemetry:PollMinutes", 5)), time);
        do
        {
            try { await PollAllAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Telemetri turu başarısız oldu."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task PollAllAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var client = scope.ServiceProvider.GetRequiredService<ISchoolClient>();
        var vault = scope.ServiceProvider.GetRequiredService<SecretVault>();
        var schools = await db.Schools.Where(s => s.Status == SchoolStatus.Active).ToListAsync(cancellationToken);
        foreach (var school in schools)
        {
            var now = time.GetUtcNow();
            try
            {
                var token = await vault.GetAsync(school.Id, SecretNames.TelemetryToken) ?? throw new InvalidOperationException("telemetri jetonu yok");
                var telemetry = await client.GetTelemetryAsync(school, token, cancellationToken);
                db.Telemetry.Add(TelemetrySnapshot.From(school.Id, telemetry, now));
                school.Seen(now);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                db.Telemetry.Add(TelemetrySnapshot.Unreachable(school.Id, ex.Message, now));
            }
        }
        // 30 günden eski kayıtlar budanır.
        var cutoff = time.GetUtcNow().AddDays(-30);
        await db.Telemetry.Where(t => t.TakenAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}

// Sunucu ölçümleri (gözlem paneli): her sunucuda ajanın `host-stats` komutu - yük, bellek, disk ve
// konteynerlerin bellek/CPU/yeniden başlama sayısı. Kapalı sunucuya bağlanılmaz; ulaşılamayan sunucu
// kırmızı kayıt düşer. 7 günden eski ölçümler budanır (eğri için yeterli, tablo büyümesin).
public class HostStatsPoller(IServiceScopeFactory scopes, IConfiguration config, TimeProvider time, ILogger<HostStatsPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(config.GetValue("Telemetry:PollMinutes", 5)), time);
        do
        {
            try { await PollAllAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Sunucu ölçüm turu başarısız oldu."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task PollAllAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var agent = scope.ServiceProvider.GetRequiredService<IHostAgent>();
        var hosts = await db.Hosts.Where(h => h.Status != HostStatus.Offline).ToListAsync(cancellationToken);
        foreach (var host in hosts)
        {
            var now = time.GetUtcNow();
            try
            {
                var result = await agent.RunAsync(host, AgentCommand.For("host-stats"), cancellationToken);
                db.HostSnapshots.Add(result.Ok ? HostSnapshot.Parse(host.Id, result.Output, now) : HostSnapshot.Unreachable(host.Id, result.Describe(), now));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                db.HostSnapshots.Add(HostSnapshot.Unreachable(host.Id, ex.Message, now));
            }
        }
        var cutoff = time.GetUtcNow().AddDays(-7);
        await db.HostSnapshots.Where(s => s.TakenAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}

// TLS bitiş tarihlerini izler (§3.3). Caddy sertifikayı kendisi yeniler; bu yalnızca güvence: yenileme
// sessizce durursa (DNS kaydı değişti, Let's Encrypt sınırı) okul kırılmadan alarm düşer. Ulaşılamayan okul
// burada alarm üretmez - onu TelemetryPoller zaten kırmızıya çevirir. Aynı okul ve türde onaylanmamış
// alarm varken yenisi yazılmaz; operatör onaylarsa ve sorun sürerse bir sonraki turda yeniden düşer.
public class CertificateWatcher(IServiceScopeFactory scopes, IConfiguration config, TimeProvider time, ILogger<CertificateWatcher> logger) : BackgroundService
{
    public const string ExpiringKind = "tls_expiring";
    public const string InvalidKind = "tls_invalid";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(config.GetValue("Certificates:CheckHours", 6)), time);
        do
        {
            try { await CheckAllAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Sertifika turu başarısız oldu."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task CheckAllAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var probe = scope.ServiceProvider.GetRequiredService<ICertificateProbe>();
        var notifier = scope.ServiceProvider.GetRequiredService<IOperatorNotifier>();
        var notices = new List<OperatorNotice>();
        var warnDays = config.GetValue("Certificates:WarnDays", 14);
        var schools = await db.Schools.Where(s => s.Status == SchoolStatus.Active).ToListAsync(cancellationToken);
        var open = (await db.Alerts
                .Where(a => a.AcknowledgedAt == null && (a.Kind == ExpiringKind || a.Kind == InvalidKind))
                .Select(a => new { a.SchoolId, a.Kind })
                .ToListAsync(cancellationToken))
            .Select(a => (a.SchoolId, a.Kind)).ToHashSet();

        foreach (var school in schools)
        {
            // Okulun bağlı kendi alan adının sertifikasını da Caddy alır ve yeniler; ikisi de izlenir.
            // Alarm okul + tür başına tektir: iki adres aynı anda bozulursa ilki yazılır.
            IEnumerable<string> domains = school.CustomDomainActive && school.CustomDomain is { } custom
                ? [school.PrimaryDomain, custom]
                : [school.PrimaryDomain];
            foreach (var domain in domains)
            {
                CertificateInfo info;
                try { info = await probe.GetAsync(domain, cancellationToken); }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation("Sertifika okunamadı: {Domain} ({Error})", domain, ex.Message);
                    continue;
                }

                var now = time.GetUtcNow();
                var daysLeft = (info.NotAfter - now).TotalDays;
                if (daysLeft < warnDays && open.Add((school.Id, ExpiringKind)))
                {
                    var detail = daysLeft <= 0
                        ? $"{domain} sertifikasının süresi {info.NotAfter:yyyy-MM-dd} tarihinde doldu."
                        : $"{domain} sertifikası {info.NotAfter:yyyy-MM-dd} tarihinde bitiyor ({Math.Floor(daysLeft)} gün). Caddy yenilemesi çalışmıyor olabilir.";
                    var level = daysLeft < 3 ? "critical" : "warning";
                    db.Alerts.Add(SchoolAlert.Create(school.Id, ExpiringKind, level, detail, now, now));
                    notices.Add(new OperatorNotice(level, school.Slug, ExpiringKind, detail));
                }
                if (!info.ChainValid && open.Add((school.Id, InvalidKind)))
                {
                    var detail = $"{domain} sertifikası doğrulanamıyor (zincir, ad uyuşmazlığı ya da kendinden imzalı).";
                    db.Alerts.Add(SchoolAlert.Create(school.Id, InvalidKind, "critical", detail, now, now));
                    notices.Add(new OperatorNotice("critical", school.Slug, InvalidKind, detail));
                }
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        await notifier.NotifyAsync(notices, cancellationToken);
    }
}

// Lisans bitişini izler (§5.7). Süresi dolan okul kendiliğinden kapanmaz (karar operatörün); bu yalnızca
// "yenilemeyi unuttum" durumunu panelde ve webhook'ta görünür kılar. Okul + tür başına tek onaylanmamış
// alarm: "bitiyor" ile "doldu" ayrı türlerdir, böylece süre dolunca düzey yükselir ve yeniden bildirilir.
public class LicenseWatcher(IServiceScopeFactory scopes, IConfiguration config, TimeProvider time, ILogger<LicenseWatcher> logger) : BackgroundService
{
    public const string ExpiringKind = "license_expiring";
    public const string ExpiredKind = "license_expired";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(config.GetValue("Licenses:CheckHours", 12)), time);
        do
        {
            try { await CheckAllAsync(stoppingToken); }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Lisans turu başarısız oldu."); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task CheckAllAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IOperatorNotifier>();
        var today = PlatformClock.Today(config, time);
        var now = time.GetUtcNow();
        var schools = await db.Schools.Where(s => s.Status == SchoolStatus.Active && s.LicenseEndsOn != null).ToListAsync(cancellationToken);
        var open = (await db.Alerts
                .Where(a => a.AcknowledgedAt == null && (a.Kind == ExpiringKind || a.Kind == ExpiredKind))
                .Select(a => new { a.SchoolId, a.Kind })
                .ToListAsync(cancellationToken))
            .Select(a => (a.SchoolId, a.Kind)).ToHashSet();

        var notices = new List<OperatorNotice>();
        foreach (var school in schools)
        {
            var state = SchoolPackage.StateOn(school.LicenseEndsOn, today);
            if (state is not (LicenseState.Expiring or LicenseState.Expired)) continue;
            var expired = state == LicenseState.Expired;
            var kind = expired ? ExpiredKind : ExpiringKind;
            if (!open.Add((school.Id, kind))) continue;

            var daysLeft = SchoolPackage.DaysLeft(school.LicenseEndsOn, today)!.Value;
            var detail = expired
                ? $"Lisans {school.LicenseEndsOn:yyyy-MM-dd} tarihinde doldu ({-daysLeft} gün önce). Okul açık kalır; Paket → Lisansı yenile."
                : $"Lisans {school.LicenseEndsOn:yyyy-MM-dd} tarihinde bitiyor ({daysLeft} gün). Paket → Lisansı yenile.";
            var level = expired ? "critical" : "warning";
            db.Alerts.Add(SchoolAlert.Create(school.Id, kind, level, detail, now, now));
            notices.Add(new OperatorNotice(level, school.Slug, kind, detail));
        }
        await db.SaveChangesAsync(cancellationToken);
        await notifier.NotifyAsync(notices, cancellationToken);
    }
}
