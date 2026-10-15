using System.Text.Json;
using Abdera.Platform.Api.Domain;
using Abdera.Platform.Api.Infrastructure;
using Abdera.Platform.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Platform.Api.Provisioning;

public record StepOutcome(bool Success, string? Log)
{
    public static StepOutcome Ok(string? log = null) => new(true, log);
    public static StepOutcome Fail(string log) => new(false, log);
}

public record StepDefinition(string Name, Func<StepContext, Task<StepOutcome>> Execute);

public record StepContext(ProvisioningRun Run, School School, int Step, int Attempt, CancellationToken CancellationToken)
{
    public string OperationId => Run.OperationId(Step, Attempt);
}

// Okul işlemlerinin adım planları (docs/multi-tenant/ARCHITECTURE.md §6, §7, §9). Her adım
// idempotenttir: yeniden denemede ya hiçbir şey değişmez ya da aynı sonuca varılır. Ajan adımları
// kalıcı işlem kimliği (--op) taşır; bağlantı koparsa yeniden deneme ajanın kaydettiği sonucu okur.
public class ProvisioningEngine(
    PlatformDbContext db,
    IHostAgent agent,
    IDnsProvider dns,
    ISchoolClient schoolClient,
    SecretVault vault,
    SchoolEnvBuilder envBuilder,
    IConfiguration config,
    TimeProvider time)
{
    public int SchoolReserveMb => config.GetValue("Capacity:SchoolReserveMb", 768);

    public IReadOnlyList<StepDefinition> PlanFor(RunKind kind) => kind switch
    {
        RunKind.Create =>
        [
            new("Ayır", _ => Task.FromResult(StepOutcome.Ok())),
            new("Sunucu seç", SelectHostAsync),
            new("Sırları üret", GenerateSecretsAsync),
            new("DNS", UpsertDnsAsync),
            new("Dosyaları yaz", WriteFilesAsync),
            new("İmajları çek", context => AgentStepAsync(context, "pull")),
            new("Başlat", context => AgentStepAsync(context, "up")),
            new("Sağlık", HealthAsync),
            new("Okulu hazırla", ProvisionSchoolAsync),
            new("Yayına al", RouteAsync),
            new("Duman testi", SmokeAsync),
            new("Bitir", FinishCreateAsync),
        ],
        RunKind.Upgrade =>
        [
            new("Güncelle", UpgradeAsync),
            new("Sağlık", HealthAsync),
            new("Bitir", FinishUpgradeAsync),
        ],
        RunKind.Suspend =>
        [
            // Ajan önce uygulamadan bağımsız şifreli döküm alır, saklama kuralı dışına kopyalar (§16.3).
            new("Son yedek ve askı", context => AgentStepAsync(context, "suspend")),
            new("Bitir", context => FinishStatusAsync(context, school => school.Suspend(time.GetUtcNow()), "school.suspended")),
        ],
        RunKind.Resume =>
        [
            new("Devam ettir", context => AgentStepAsync(context, "resume")),
            new("Bitir", context => FinishStatusAsync(context, school => school.Resume(time.GetUtcNow()), "school.resumed")),
        ],
        RunKind.Destroy =>
        [
            // 1. aşama: veri silinmez, 30 günlük bekleme ajan tarafında başlar (§16.9).
            new("Silme bekleyişi", context => AgentStepAsync(context, "destroy")),
            new("Bitir", context => FinishStatusAsync(context, school => school.MarkPendingDestroy(time.GetUtcNow()), "school.destroy_requested")),
        ],
        RunKind.Undestroy =>
        [
            new("Silmeyi geri al", context => AgentStepAsync(context, "undestroy")),
            new("Bitir", context => FinishStatusAsync(context, school => school.RestoreFromPendingDestroy(time.GetUtcNow()), "school.destroy_cancelled")),
        ],
        RunKind.Purge =>
        [
            // Süre dolmadıysa ajan reddeder; platform bu bekleyişi kısaltamaz.
            new("Kalıcı sil", context => AgentStepAsync(context, "purge")),
            new("DNS kaydını kaldır", DeleteDnsAsync),
            new("Kapasiteyi bırak", ReleaseHostAsync),
            new("Bitir", context => FinishStatusAsync(context, school => school.MarkDestroyed(time.GetUtcNow()), "school.purged")),
        ],
        RunKind.Cancel =>
        [
            // Yarıda kalan kurulumu geri alır. Okul hiç veri almadıysa (provision adımı geçilmediyse)
            // ajan beklemeden siler; aldıysa purge reddedilir ve işlem başarısız olur.
            new("Yönlendirmeyi kaldır", RemoveRouteIfAnyAsync),
            new("Durdur", context => AgentIfHostAsync(context, "destroy")),
            new("Sil", context => AgentIfHostAsync(context, "purge")),
            new("DNS kaydını kaldır", DeleteDnsAsync),
            new("Kapasiteyi bırak", ReleaseHostAsync),
            new("Bitir", context => FinishStatusAsync(context, school => school.MarkDestroyed(time.GetUtcNow()), "school.provisioning_cancelled")),
        ],
        RunKind.Reconfigure =>
        [
            // Modül veya entegrasyon değişikliği: yeni .env; sağlıksızsa ajan önceki yapılandırmaya döner.
            new("Yapılandırmayı uygula", ReconfigureAsync),
            new("Sağlık", HealthAsync),
            new("Bitir", context => FinishStatusAsync(context, _ => { }, "school.reconfigured")),
        ],
        RunKind.Drill =>
        [
            // Geri yükleme provası (§16.7): uygulamadan bağımsız döküm, aynı sunucuda <slug>-drill
            // kopyasına dış bağlantılar KAPALI geri yüklenir; yayına alınmaz.
            new("Döküm al", context => AgentStepAsync(context, "dump")),
            new("Prova kopyası", DrillRestoreAsync),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private async Task<ServerHost> HostOf(School school) =>
        school.HostId is { } hostId
            ? await db.Hosts.SingleAsync(host => host.Id == hostId)
            : throw new InvalidOperationException("Okula sunucu atanmadı.");

    private async Task<StepOutcome> AgentStepAsync(StepContext context, string verb, IEnumerable<string>? args = null, string? stdin = null)
    {
        var host = await HostOf(context.School);
        var command = AgentCommand.For(verb, context.School.Slug, args, context.OperationId, stdin);
        var result = await agent.RunAsync(host, command, context.CancellationToken);
        return result.Ok ? StepOutcome.Ok(Trim(result.Output)) : StepOutcome.Fail(result.Describe());
    }

    private async Task<StepOutcome> AgentIfHostAsync(StepContext context, string verb) =>
        context.School.HostId is null ? StepOutcome.Ok("sunucu atanmamıştı") : await AgentStepAsync(context, verb);

    // §6 adım 2: rezerv belleği yeterli sunucu; xmin sayesinde iki kurulum aynı son yeri alamaz
    // (yarışı kaybeden SaveChanges'te DbUpdateConcurrencyException alır, adım yeniden denenir).
    private async Task<StepOutcome> SelectHostAsync(StepContext context)
    {
        if (context.School.HostId is { } existing)
            return StepOutcome.Ok($"zaten atanmış: {(await db.Hosts.SingleAsync(h => h.Id == existing)).Name}");

        // Yarışı kaybeden kurulum güncel kapasiteyi okuyup yeniden seçer (birkaç deneme).
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidates = await db.Hosts.Where(host => host.Status == HostStatus.Active).OrderBy(host => host.ReservedMemoryMb).ToListAsync(context.CancellationToken);
            var host = candidates.FirstOrDefault(candidate => candidate.CanFit(SchoolReserveMb));
            if (host is null) return StepOutcome.Fail("Hiçbir sunucuda yer yok; yeni sunucu ekleyin.");

            var now = time.GetUtcNow();
            host.Reserve(SchoolReserveMb, now);
            context.School.AssignHost(host.Id, now);
            try
            {
                await db.SaveChangesAsync(context.CancellationToken);
                return StepOutcome.Ok($"{host.Name} ({host.ReservedMemoryMb}/{host.CapacityMb} MB)");
            }
            catch (DbUpdateConcurrencyException ex) when (ex.Entries.All(entry => entry.Entity is ServerHost))
            {
                foreach (var entry in ex.Entries) await entry.ReloadAsync(context.CancellationToken);
            }
        }
        return StepOutcome.Fail("Sunucu kapasitesi yarışı çözülemedi; yeniden deneyin.");
    }

    private async Task<StepOutcome> GenerateSecretsAsync(StepContext context)
    {
        var id = context.School.Id;
        await vault.GetOrCreateAsync(id, SecretNames.PostgresPassword, () => SecretVault.RandomToken(32));
        await vault.GetOrCreateAsync(id, SecretNames.BackupEncryptionKey, () => SecretVault.RandomKeyBase64(32));
        await vault.GetOrCreateAsync(id, SecretNames.BackupGatewayPassword, () => SecretVault.RandomToken(24));
        await vault.GetOrCreateAsync(id, SecretNames.ProvisioningToken, () => SecretVault.RandomToken(32));
        await vault.GetOrCreateAsync(id, SecretNames.TelemetryToken, () => SecretVault.RandomToken(32));
        await vault.GetOrCreateAsync(id, SecretNames.AlertsSecret, () => SecretVault.RandomToken(32));
        await vault.GetOrCreateAsync(id, SecretNames.WhatsAppPayloadSigningKey, () => SecretVault.RandomToken(32));
        await db.SaveChangesAsync(context.CancellationToken);
        return StepOutcome.Ok("sırlar hazır");
    }

    private bool IsManagedDomain(School school) =>
        config["Dns:BaseDomain"] is { Length: > 0 } baseDomain && school.PrimaryDomain.EndsWith("." + baseDomain, StringComparison.Ordinal);

    private async Task<StepOutcome> UpsertDnsAsync(StepContext context)
    {
        if (!IsManagedDomain(context.School)) return StepOutcome.Ok("özel alan adı: okul kendi DNS'inde yönlendirir");
        var host = await HostOf(context.School);
        await dns.UpsertARecordAsync(context.School.PrimaryDomain, host.PublicIp, context.CancellationToken);
        return StepOutcome.Ok($"{context.School.PrimaryDomain} → {host.PublicIp}");
    }

    private async Task<StepOutcome> DeleteDnsAsync(StepContext context)
    {
        if (!IsManagedDomain(context.School)) return StepOutcome.Ok("özel alan adı");
        await dns.DeleteRecordAsync(context.School.PrimaryDomain, context.CancellationToken);
        return StepOutcome.Ok("kaldırıldı");
    }

    private async Task<Release> ReleaseFor(string? version) =>
        await db.Releases.SingleOrDefaultAsync(release => release.Version == version)
        ?? throw new InvalidOperationException($"Sürüm kayıtlı değil: {version}");

    private async Task<StepOutcome> WriteFilesAsync(StepContext context)
    {
        var release = await ReleaseFor(context.School.TargetVersion ?? context.School.AppVersion);
        var env = envBuilder.Build(context.School, release, await vault.GetAllAsync(context.School.Id));
        return await AgentStepAsync(context, "create", stdin: env);
    }

    private async Task<StepOutcome> ReconfigureAsync(StepContext context)
    {
        var release = await ReleaseFor(context.School.AppVersion);
        var env = envBuilder.Build(context.School, release, await vault.GetAllAsync(context.School.Id));
        return await AgentStepAsync(context, "reconfigure", stdin: env);
    }

    private async Task<StepOutcome> DrillRestoreAsync(StepContext context)
    {
        var dumpPath = await db.Steps
            .Where(s => s.RunId == context.Run.Id && s.Step == context.Step - 1 && s.Status == StepStatus.Succeeded)
            .OrderByDescending(s => s.Attempt).Select(s => s.Log).FirstOrDefaultAsync(context.CancellationToken);
        if (string.IsNullOrWhiteSpace(dumpPath)) return StepOutcome.Fail("döküm adı bulunamadı");
        var dumpName = Path.GetFileName(dumpPath.Trim().Split('\n').Last());
        return await AgentStepAsync(context, "restore", [dumpName, "--drill"]);
    }

    private async Task<StepOutcome> HealthAsync(StepContext context)
    {
        var host = await HostOf(context.School);
        var result = await agent.RunAsync(host, AgentCommand.For("status", context.School.Slug), context.CancellationToken);
        if (!result.Ok) return StepOutcome.Fail(result.Describe());
        var healthy = JsonDocument.Parse(result.Output).RootElement.GetProperty("healthy").GetBoolean();
        return healthy ? StepOutcome.Ok(Trim(result.Output)) : StepOutcome.Fail("okul sağlıklı değil: " + Trim(result.Output));
    }

    private async Task<StepOutcome> ProvisionSchoolAsync(StepContext context)
    {
        var request = await vault.GetAsync(context.School.Id, SecretNames.ProvisionRequest)
            ?? throw new InvalidOperationException("Kurulum isteği bulunamadı.");
        return await AgentStepAsync(context, "provision", stdin: request);
    }

    // Ajan sözdizimi: route add <slug> <alan> (sunucu düzeyinde kilit + caddy validate, §16.2).
    private Task<StepOutcome> RouteAsync(StepContext context) =>
        AgentStepAsync(context, "route add", [context.School.PrimaryDomain]);

    private async Task<StepOutcome> RemoveRouteIfAnyAsync(StepContext context)
    {
        if (context.School.HostId is null) return StepOutcome.Ok("sunucu atanmamıştı");
        var host = await HostOf(context.School);
        var command = AgentCommand.For("route remove", context.School.Slug, operationId: context.OperationId);
        var result = await agent.RunAsync(host, command, context.CancellationToken);
        // Okul hiç oluşturulmadıysa (create adımına gelinmediyse) ajan "okul yok" der: kaldırılacak yönlendirme yok.
        return result.Ok || result.ExitCode == 4 ? StepOutcome.Ok("yönlendirme yok") : StepOutcome.Fail(result.Describe());
    }

    // TLS sertifikası ve DNS yayılması zaman alabilir: birkaç deneme, aralarında bekleme.
    private async Task<StepOutcome> SmokeAsync(StepContext context)
    {
        var attempts = config.GetValue("Provisioning:SmokeAttempts", 12);
        var delay = TimeSpan.FromSeconds(config.GetValue("Provisioning:SmokeDelaySeconds", 15));
        string? problem = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                problem = await schoolClient.SmokeTestAsync(context.School, context.CancellationToken);
                if (problem is null) return StepOutcome.Ok($"https://{context.School.PrimaryDomain} yanıt veriyor");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                problem = ex.Message;
            }
            if (attempt < attempts) await Task.Delay(delay, time, context.CancellationToken);
        }
        return StepOutcome.Fail($"duman testi geçmedi: {problem}");
    }

    private async Task<StepOutcome> FinishCreateAsync(StepContext context)
    {
        var backup = await AgentStepAsync(context, "backup-now");
        var version = context.School.TargetVersion!;
        var now = time.GetUtcNow();
        context.School.MarkActive(version, now);
        // Kurulum isteği (geçici parola dahil) artık gerekmez; parola ayrı sır olarak kalır.
        await db.SchoolSecrets.Where(s => s.SchoolId == context.School.Id && s.Name == SecretNames.ProvisionRequest).ExecuteDeleteAsync(context.CancellationToken);
        db.AuditLogs.Add(PlatformAuditLog.Record(null, "school.activated", context.School.Id, now,
            afterJson: JsonSerializer.Serialize(new { context.School.Slug, version, context.School.PrimaryDomain })));
        return backup.Success ? StepOutcome.Ok("okul etkin; ilk yedek tetiklendi") : StepOutcome.Ok("okul etkin; ilk yedek tetiklenemedi: " + backup.Log);
    }

    private async Task<StepOutcome> UpgradeAsync(StepContext context)
    {
        var release = await ReleaseFor(context.Run.TargetVersion);
        return await AgentStepAsync(context, "upgrade", [release.ApiImage, release.WebImage]);
    }

    private Task<StepOutcome> FinishUpgradeAsync(StepContext context)
    {
        var now = time.GetUtcNow();
        var from = context.School.AppVersion;
        context.School.CompleteUpgrade(context.Run.TargetVersion!, now);
        db.AuditLogs.Add(PlatformAuditLog.Record(null, "school.upgraded", context.School.Id, now,
            beforeJson: JsonSerializer.Serialize(new { version = from }),
            afterJson: JsonSerializer.Serialize(new { version = context.Run.TargetVersion, rolloutId = context.Run.RolloutId })));
        return Task.FromResult(StepOutcome.Ok($"{from} → {context.Run.TargetVersion}"));
    }

    private async Task<StepOutcome> ReleaseHostAsync(StepContext context)
    {
        if (context.School.HostId is not { } hostId) return StepOutcome.Ok("sunucu atanmamıştı");
        var host = await db.Hosts.SingleAsync(h => h.Id == hostId, context.CancellationToken);
        host.Release(SchoolReserveMb, time.GetUtcNow());
        return StepOutcome.Ok($"{host.Name}: {host.ReservedMemoryMb}/{host.CapacityMb} MB");
    }

    private Task<StepOutcome> FinishStatusAsync(StepContext context, Action<School> apply, string action)
    {
        apply(context.School);
        db.AuditLogs.Add(PlatformAuditLog.Record(null, action, context.School.Id, time.GetUtcNow(),
            afterJson: JsonSerializer.Serialize(new { context.School.Slug, status = context.School.Status.ToString() })));
        return Task.FromResult(StepOutcome.Ok(context.School.Status.ToString()));
    }

    private static string? Trim(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        return trimmed.Length > 2000 ? trimmed[..2000] : trimmed;
    }
}
