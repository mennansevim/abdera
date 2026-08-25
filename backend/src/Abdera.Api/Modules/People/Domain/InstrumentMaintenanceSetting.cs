using Abdera.Api.Shared;

namespace Abdera.Api.Modules.People.Domain;

public enum MaintenanceNotificationPreference
{
    None,
    WhatsApp,
}

public class InstrumentMaintenanceSetting
{
    public Guid Id { get; private set; }
    public Guid InstrumentId { get; private set; }
    public string MaintenanceType { get; private set; } = null!;
    public int PeriodDays { get; private set; }
    public bool IsEnabled { get; private set; }
    public MaintenanceNotificationPreference NotificationPreference { get; private set; }
    public DateTimeOffset NextReminderAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private InstrumentMaintenanceSetting() { }

    public static InstrumentMaintenanceSetting Create(
        Guid instrumentId, string maintenanceType, int periodDays, bool isEnabled,
        MaintenanceNotificationPreference preference, DateTimeOffset nextReminderAt, DateTimeOffset now)
    {
        var setting = new InstrumentMaintenanceSetting { Id = Guid.NewGuid(), InstrumentId = instrumentId };
        setting.Update(maintenanceType, periodDays, isEnabled, preference, nextReminderAt, now);
        return setting;
    }

    public void Update(
        string maintenanceType, int periodDays, bool isEnabled,
        MaintenanceNotificationPreference preference, DateTimeOffset nextReminderAt, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(maintenanceType) || maintenanceType.Trim().Length > 200)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["maintenanceType"] = ["Bakım türü 1–200 karakter olmalı."] });
        if (periodDays is < 1 or > 3650)
            throw new ValidationFailedException(new Dictionary<string, string[]> { ["periodDays"] = ["Bakım dönemi 1–3650 gün arasında olmalı."] });

        MaintenanceType = maintenanceType.Trim();
        PeriodDays = periodDays;
        IsEnabled = isEnabled;
        NotificationPreference = preference;
        NextReminderAt = nextReminderAt;
        UpdatedAt = now;
    }

    public void AdvanceAfter(DateTimeOffset now)
    {
        do NextReminderAt = NextReminderAt.AddDays(PeriodDays);
        while (NextReminderAt <= now);
        UpdatedAt = now;
    }
}

public class InstrumentMaintenanceReminder
{
    public Guid Id { get; private set; }
    public Guid SettingId { get; private set; }
    public Guid GuardianId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private InstrumentMaintenanceReminder() { }

    public static InstrumentMaintenanceReminder Create(Guid settingId, Guid guardianId, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        SettingId = settingId,
        GuardianId = guardianId,
        CreatedAt = now,
    };
}
