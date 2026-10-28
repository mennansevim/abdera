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
    // Gözlem paneli (yalnızca adet; eski okul sürümü göndermez): son 24 saatin hata/uyarı sayısı ve
    // veritabanı bağlantı/kilit/migration bilgisi.
    public int? Errors24h { get; private set; }
    public int? Warnings24h { get; private set; }
    public DateTimeOffset? LastErrorAt { get; private set; }
    public int? DbConnections { get; private set; }
    public int? DbWaitingOnLocks { get; private set; }
    public string? LastMigration { get; private set; }
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
        Errors24h = telemetry.Logs?.Errors24h,
        Warnings24h = telemetry.Logs?.Warnings24h,
        LastErrorAt = telemetry.Logs?.LastErrorAt,
        DbConnections = telemetry.Database?.Connections,
        DbWaitingOnLocks = telemetry.Database?.WaitingOnLocks,
        LastMigration = telemetry.Database?.LastMigration is { Length: > 150 } m ? m[..150] : telemetry.Database?.LastMigration,
    };
}

// Okulun /api/instance/telemetry cevabı (backend Modules/Instance/Features/InstanceTelemetry.cs).
public record SchoolTelemetry(
    string? Version,
    SchoolTelemetry.HealthPart? Health,
    SchoolTelemetry.CountsPart? Counts,
    SchoolTelemetry.BackupPart? Backup,
    SchoolTelemetry.NotificationsPart? Notifications,
    long? DatabaseSizeBytes,
    SchoolTelemetry.LogsPart? Logs = null,
    SchoolTelemetry.DatabasePart? Database = null)
{
    public record LogsPart(int Errors24h, int Warnings24h, DateTimeOffset? LastErrorAt);
    public record DatabasePart(int Connections, int WaitingOnLocks, string? LastMigration);
    public record HealthPart(string? Level, string? Detail);
    public record CountsPart(int ActiveStudents, int ActiveTeachers, int ActiveEnrollments, int? ActiveStaffUsers = null);
    public record BackupPart(DateTimeOffset? LastSucceededAt);
    public record NotificationsPart(int Pending, int Failed);
}
