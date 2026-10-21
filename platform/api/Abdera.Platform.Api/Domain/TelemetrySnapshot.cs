namespace Abdera.Platform.Api.Domain;

// Okulun son durumu (§3.2): yalnızca sayılar ve sağlık; kişi verisi yok.
public class TelemetrySnapshot
{
    public Guid Id { get; private set; }
    public Guid SchoolId { get; private set; }
    public DateTimeOffset TakenAt { get; private set; }
    public bool Reachable { get; private set; }
    public string? Health { get; private set; }
    public string? HealthDetail { get; private set; }
    public string? AppVersion { get; private set; }
    public int? ActiveStudents { get; private set; }
    public int? ActiveTeachers { get; private set; }
    public int? ActiveEnrollments { get; private set; }
    // Paket sınırına sayılan etkin personel hesabı (yönetici + öğretmen); eski okul sürümü göndermez.
    public int? ActiveStaffUsers { get; private set; }
    public DateTimeOffset? LastBackupAt { get; private set; }
    public long? DatabaseSizeBytes { get; private set; }
    public int? PendingNotifications { get; private set; }
    public int? FailedNotifications { get; private set; }
    public string? Error { get; private set; }

    private TelemetrySnapshot() { }

    public static TelemetrySnapshot Unreachable(Guid schoolId, string error, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        SchoolId = schoolId,
        TakenAt = now,
        Reachable = false,
        Error = error.Length > 500 ? error[..500] : error,
    };

    public static TelemetrySnapshot From(Guid schoolId, SchoolTelemetry telemetry, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        SchoolId = schoolId,
        TakenAt = now,
        Reachable = true,
        Health = telemetry.Health?.Level,
        HealthDetail = telemetry.Health?.Detail,
        AppVersion = telemetry.Version,
        ActiveStudents = telemetry.Counts?.ActiveStudents,
        ActiveTeachers = telemetry.Counts?.ActiveTeachers,
        ActiveEnrollments = telemetry.Counts?.ActiveEnrollments,
        ActiveStaffUsers = telemetry.Counts?.ActiveStaffUsers,
        LastBackupAt = telemetry.Backup?.LastSucceededAt,
        DatabaseSizeBytes = telemetry.DatabaseSizeBytes,
        PendingNotifications = telemetry.Notifications?.Pending,
        FailedNotifications = telemetry.Notifications?.Failed,
    };
}

// Okulun /api/instance/telemetry cevabı (backend Modules/Instance/Features/InstanceTelemetry.cs).
public record SchoolTelemetry(
    string? Version,
    SchoolTelemetry.HealthPart? Health,
    SchoolTelemetry.CountsPart? Counts,
    SchoolTelemetry.BackupPart? Backup,
    SchoolTelemetry.NotificationsPart? Notifications,
    long? DatabaseSizeBytes)
{
    public record HealthPart(string? Level, string? Detail);
    public record CountsPart(int ActiveStudents, int ActiveTeachers, int ActiveEnrollments, int? ActiveStaffUsers = null);
    public record BackupPart(DateTimeOffset? LastSucceededAt);
    public record NotificationsPart(int Pending, int Failed);
}
