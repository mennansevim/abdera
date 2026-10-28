namespace Abdera.Api.Modules.Ops.Domain;

// #31: arka plan işlerinin her turu bir hata yakalayıp loglayarak devam ediyordu - aylık aidat
// üretimi bir ay boyunca düşse kimse fark etmezdi. Her iş tur sonunda kendi satırını günceller
// (JobHeartbeatRecorder), SystemHealthMonitor yedek yaşı gibi bunu da değerlendirir.
// İş başına tek satır; geçmiş tutulmaz (hata ayrıntısı loglarda).
public class JobHeartbeat
{
    public const int MaxErrorLength = 500;

    public string JobName { get; private set; } = null!;
    public DateTimeOffset? LastSucceededAt { get; private set; }
    public DateTimeOffset? LastFailedAt { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private JobHeartbeat() { }

    // Yalnızca testler ve okuma tarafı için - yazma JobHeartbeatRecorder'ın upsert'üyle olur.
    public static JobHeartbeat Create(string jobName, DateTimeOffset? lastSucceededAt, DateTimeOffset? lastFailedAt, string? lastError, DateTimeOffset updatedAt) =>
        new() { JobName = jobName, LastSucceededAt = lastSucceededAt, LastFailedAt = lastFailedAt, LastError = lastError, CreatedAt = updatedAt, UpdatedAt = updatedAt };

    public static string? TrimError(string? error) =>
        error is { Length: > MaxErrorLength } ? error[..MaxErrorLength] : error;
}

public record BackgroundJob(string Name, string Label, TimeSpan Interval);

// Beklenen aralıklar işlerin kendi PeriodicTimer'larıyla aynı olmalı.
public static class BackgroundJobs
{
    public static readonly BackgroundJob NotificationDispatcher = new("notification-dispatcher", "WhatsApp bildirim gönderimi", TimeSpan.FromMinutes(1));
    public static readonly BackgroundJob OverdueReceivables = new("overdue-receivables", "Vadesi geçen aidat taraması", TimeSpan.FromHours(1));
    public static readonly BackgroundJob MonthlyReceivables = new("monthly-receivables", "Aylık aidat üretimi", TimeSpan.FromHours(24));
    public static readonly BackgroundJob AttendanceReminder = new("attendance-reminder", "Yoklama hatırlatması", TimeSpan.FromMinutes(10));
    public static readonly BackgroundJob LessonNoteReminder = new("lesson-note-reminder", "Ders yorumu hatırlatması", TimeSpan.FromHours(3));
    public static readonly BackgroundJob AudioClipMaintenance = new("audio-clip-maintenance", "Ses Defteri bakımı ve yedeği", TimeSpan.FromHours(6));

    public static readonly IReadOnlyList<BackgroundJob> All =
        [NotificationDispatcher, OverdueReceivables, MonthlyReceivables, AttendanceReminder, LessonNoteReminder, AudioClipMaintenance];

    // Bir tur kaçabilir (deploy, kısa kesinti); üç tur üst üste başarısızsa sorun var demektir.
    public const int MissedRunsBeforeAlert = 3;

    // Saf karar fonksiyonu (SystemHealthMonitor.Evaluate ile aynı desen). `runningSince`: bu
    // sürecin açılış anı - yeni açılmış bir süreçte henüz ilk turunu atmamış iş sorun değildir
    // (yoklama hatırlatması ilk turunu açılıştan 10 dk, ders yorumu 3 saat sonra atar).
    public static (SystemHealthLevel Level, string? Detail) Evaluate(
        IReadOnlyCollection<JobHeartbeat> heartbeats, DateTimeOffset now, DateTimeOffset runningSince)
    {
        var problems = new List<string>();
        foreach (var job in All)
        {
            var allowed = job.Interval * MissedRunsBeforeAlert;
            var beat = heartbeats.FirstOrDefault(h => h.JobName == job.Name);
            var lastSuccess = beat?.LastSucceededAt;

            if (beat?.LastFailedAt is { } failedAt && (lastSuccess is null || failedAt > lastSuccess))
            {
                problems.Add($"{job.Label} son turunda hata verdi: {beat.LastError}");
                continue;
            }

            // Süreç bu iş için izin verilen süreden daha kısa zamandır açıksa iş henüz
            // fırsat bulamamış olabilir - açılıştan önceki eski bir başarı da yeterli sayılır.
            if (now - runningSince < allowed) continue;

            if (lastSuccess is null)
                problems.Add($"{job.Label} hiç başarıyla çalışmadı.");
            else if (now - lastSuccess.Value > allowed)
                problems.Add($"{job.Label} {FormatAge(now - lastSuccess.Value)} önce son kez başarıyla çalıştı.");
        }

        return problems.Count == 0
            ? (SystemHealthLevel.Healthy, null)
            : (SystemHealthLevel.Degraded, string.Join(" ", problems));
    }

    private static string FormatAge(TimeSpan age) =>
        age.TotalHours >= 48 ? $"{age.TotalDays:0} gün"
        : age.TotalMinutes >= 120 ? $"{age.TotalHours:0} saat"
        : $"{age.TotalMinutes:0} dakika";
}
