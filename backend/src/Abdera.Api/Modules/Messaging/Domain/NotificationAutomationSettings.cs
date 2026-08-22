using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Domain;

// Faz 3 (docs/15-product-phases.md): admin panelden değiştirilebilir tek satırlık kurum ayarı -
// ders hatırlatmasının kaç dakika önce gideceği, otomasyonun açık/kapalı olduğu ve üçüncü RSVP
// seçeneğinin (AttendingLate) aktif olup olmadığı. Önceden appsettings'ten sabit okunuyordu
// (Notifications:LessonReminderMinutesBefore) - admin arayüzden değiştirebilsin diye DB'ye taşındı.
// CLAUDE.md'nin Hangfire/Quartz yasağı nedeniyle mevcut INotificationScheduler/NotificationDispatcher
// (BackgroundService) mimarisi değişmedi, yalnızca bu ayarı okuyan bir kaynak eklendi.
public class NotificationAutomationSettings
{
    // Tek satırlık ayar - sabit bir id ile "singleton" davranışı sağlanıyor, ayrı bir
    // "tek satır var mı" kilitleme mekanizmasına gerek kalmıyor.
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-0000000000a5");
    private static readonly int[] AllowedMinutes = [15, 30, 45, 60];

    public Guid Id { get; private set; }
    public int LessonReminderMinutesBefore { get; private set; } = 60;
    public bool IsEnabled { get; private set; } = true;
    public bool AllowAttendingLateResponse { get; private set; } = true;
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private NotificationAutomationSettings() { }

    public static NotificationAutomationSettings CreateDefault(DateTimeOffset now) => new()
    {
        Id = SingletonId,
        LessonReminderMinutesBefore = 60,
        IsEnabled = true,
        AllowAttendingLateResponse = true,
        UpdatedAt = now,
    };

    // DB'de hiç satır yoksa (henüz kimse ayarı değiştirmedi) varsayılan değerlerle geçici
    // (kalıcı olmayan) bir örnek döner - salt okuma amaçlı çağrılarda gereksiz bir Add/Save
    // tetiklemez. Yalnızca PUT ("Update") akışı gerçekten kalıcı hâle getirir.
    public static async Task<NotificationAutomationSettings> GetCurrentAsync(AbderaDbContext db) =>
        await db.NotificationAutomationSettings.SingleOrDefaultAsync(s => s.Id == SingletonId)
        ?? CreateDefault(DateTimeOffset.MinValue);

    public void Update(int lessonReminderMinutesBefore, bool isEnabled, bool allowAttendingLateResponse, Guid? updatedBy, DateTimeOffset now)
    {
        if (!AllowedMinutes.Contains(lessonReminderMinutesBefore))
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["lessonReminderMinutesBefore"] = ["Hatırlatma süresi 15, 30, 45 veya 60 dakikadan biri olmalı."],
            });
        }

        LessonReminderMinutesBefore = lessonReminderMinutesBefore;
        IsEnabled = isEnabled;
        AllowAttendingLateResponse = allowAttendingLateResponse;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
