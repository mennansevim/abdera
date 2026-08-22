using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Ops.Domain;

// Faz 4: "ana ekranda göster, sorun varsa kırmızı uyar + mail at" isteğinin tek satırlık
// kalıcı durumu. NotificationAutomationSettings ile aynı singleton desen (sabit Id) -
// SystemHealthMonitor (BackgroundService) periyodik olarak günceller, dashboard bunu okur.
public enum SystemHealthLevel
{
    Healthy,
    Degraded,
    Unhealthy,
}

public class SystemHealthStatus
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-0000000000b4");

    public Guid Id { get; private set; }
    public SystemHealthLevel Level { get; private set; } = SystemHealthLevel.Healthy;
    public string? Detail { get; private set; }
    public DateTimeOffset LastCheckedAt { get; private set; }
    public DateTimeOffset? LastAlertSentAt { get; private set; }

    private SystemHealthStatus() { }

    public static SystemHealthStatus CreateDefault(DateTimeOffset now) => new()
    {
        Id = SingletonId,
        Level = SystemHealthLevel.Healthy,
        LastCheckedAt = now,
    };

    // Salt okuma amaçlı çağrılarda (dashboard'daki GET) gereksiz bir Add/Save tetiklemez -
    // NotificationAutomationSettings.GetCurrentAsync ile aynı desen.
    public static async Task<SystemHealthStatus> GetCurrentAsync(AbderaDbContext db) =>
        await db.SystemHealthStatuses.SingleOrDefaultAsync(s => s.Id == SingletonId)
        ?? CreateDefault(DateTimeOffset.MinValue);

    public void Update(SystemHealthLevel level, string? detail, DateTimeOffset now)
    {
        Level = level;
        Detail = detail;
        LastCheckedAt = now;
    }

    public bool ShouldSendAlert(DateTimeOffset now, TimeSpan cooldown) =>
        Level != SystemHealthLevel.Healthy && (LastAlertSentAt is null || now - LastAlertSentAt > cooldown);

    public void MarkAlertSent(DateTimeOffset now) => LastAlertSentAt = now;
}
