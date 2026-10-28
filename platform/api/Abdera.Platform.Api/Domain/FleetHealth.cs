namespace Abdera.Platform.Api.Domain;

// Gözlem panelinin "ne bozuk?" kuralları - hangi durumun sorun sayıldığının tek yeri. Panel yalnızca
// sonucu gösterir. Kritik: okul ya da sunucu şu an iş görmüyor ya da veri kaybı riski var; uyarı: bakılmalı.
public static class FleetHealth
{
    public const int BackupStaleHours = 26;   // günlük yedek + 2 saat pay
    public const decimal DiskCriticalPercent = 10m;
    public const decimal DiskWarningPercent = 20m;
    public const decimal MemoryWarningPercent = 10m;
    public static readonly TimeSpan HostStatsStale = TimeSpan.FromMinutes(20);

    public enum Level { Warning, Critical }
    public record Issue(Level Severity, string Kind, string Subject, Guid? SchoolId, Guid? HostId, string Detail);

    public static IEnumerable<Issue> ForSchool(School school, TelemetrySnapshot? latest, IReadOnlyList<ContainerStat> containers, DateOnly today, DateTimeOffset now)
    {
        var name = school.DisplayName;
        if (school.Status == SchoolStatus.Failed) yield return new(Level.Critical, "run-failed", name, school.Id, null, "Son işlem başarısız oldu.");
        switch (SchoolPackage.StateOn(school.LicenseEndsOn, today))
        {
            case LicenseState.Expired: yield return new(Level.Critical, "license", name, school.Id, null, "Lisansın süresi doldu."); break;
            case LicenseState.Expiring: yield return new(Level.Warning, "license", name, school.Id, null, $"Lisansın bitmesine {SchoolPackage.DaysLeft(school.LicenseEndsOn, today)} gün kaldı."); break;
        }
        if (school.Status != SchoolStatus.Active) yield break;

        if (latest is null) { yield return new(Level.Warning, "no-telemetry", name, school.Id, null, "Henüz telemetri yok."); yield break; }
        if (!latest.Reachable) { yield return new(Level.Critical, "unreachable", name, school.Id, null, $"Ulaşılamıyor: {latest.Error}"); yield break; }
        if (latest.Health == "Unhealthy") yield return new(Level.Critical, "health", name, school.Id, null, latest.HealthDetail ?? "Okul kendini sağlıksız bildiriyor.");
        else if (latest.Health == "Degraded") yield return new(Level.Warning, "health", name, school.Id, null, latest.HealthDetail ?? "Okul kısmen çalışıyor.");
        if (latest.LastBackupAt is null)
            yield return new(Level.Critical, "backup", name, school.Id, null, "Hiç başarılı yedek yok.");
        else if (now - latest.LastBackupAt.Value > TimeSpan.FromHours(BackupStaleHours))
            yield return new(Level.Critical, "backup", name, school.Id, null, $"Son başarılı yedek {(int)(now - latest.LastBackupAt.Value).TotalHours} saat önce.");
        if (latest.Errors24h is > 0) yield return new(Level.Warning, "errors", name, school.Id, null, $"Son 24 saatte {latest.Errors24h} hata.");
        if (latest.FailedNotifications is > 0) yield return new(Level.Warning, "notifications", name, school.Id, null, $"{latest.FailedNotifications} bildirim gönderilemedi.");
        if (latest.DbWaitingOnLocks is > 0) yield return new(Level.Warning, "db-locks", name, school.Id, null, $"Veritabanında {latest.DbWaitingOnLocks} sorgu kilit bekliyor.");
        foreach (var container in containers)
        {
            if (container.Status != "running") yield return new(Level.Critical, "container", name, school.Id, null, $"{container.Name} çalışmıyor ({container.Status}).");
            else if (container.Health == "unhealthy") yield return new(Level.Critical, "container", name, school.Id, null, $"{container.Name} sağlıksız.");
        }
    }

    public static IEnumerable<Issue> ForHost(ServerHost host, HostSnapshot? latest, DateTimeOffset now)
    {
        if (host.Status == HostStatus.Offline) yield break;
        if (latest is null || now - latest.TakenAt > HostStatsStale)
        {
            yield return new(Level.Warning, "host-stale", host.Name, null, host.Id, "Güncel ölçüm yok.");
            yield break;
        }
        if (!latest.Reachable) { yield return new(Level.Critical, "host-unreachable", host.Name, null, host.Id, $"Sunucuya ulaşılamıyor: {latest.Error}"); yield break; }
        if (Percent(latest.DiskFreeMb, latest.DiskTotalMb) is { } disk)
        {
            if (disk < DiskCriticalPercent) yield return new(Level.Critical, "disk", host.Name, null, host.Id, $"Diskte %{disk:0} boş yer kaldı.");
            else if (disk < DiskWarningPercent) yield return new(Level.Warning, "disk", host.Name, null, host.Id, $"Diskte %{disk:0} boş yer kaldı.");
        }
        if (Percent(latest.MemAvailableMb, latest.MemTotalMb) is < MemoryWarningPercent and { } memory)
            yield return new(Level.Warning, "memory", host.Name, null, host.Id, $"Boş bellek %{memory:0}.");
        if (latest.Load5 is { } load && latest.Cpus is > 0 and { } cpus && load > cpus)
            yield return new(Level.Warning, "load", host.Name, null, host.Id, $"5 dakikalık yük {load:0.##}, çekirdek {cpus}.");
    }

    public static decimal? Percent(int? part, int? total) =>
        part is { } p && total is > 0 and { } t ? decimal.Round(100m * p / t, 1) : null;
}
