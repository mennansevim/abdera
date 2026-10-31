namespace Abdera.Api.Modules.Ops.Domain;

public enum DataExportStatus
{
    Pending,
    Running,
    Completed,
    Failed,
}

// Tüm verinin Excel dökümü (docs/10-decisions.md AJ). İstek satırı hemen açılır, dosyayı
// DataExportWorker arka planda üretir. Yalnızca son KeepLatest döküm tutulur: dosya her
// istekte baştan üretildiği için eskisini saklamanın anlamı yok ve her döküm okulun bütün
// kişisel verisinin bir kopyası. Finansal/audit kaydı değil, silinebilir.
public class DataExport
{
    public const int KeepLatest = 3;
    public const int MaxErrorLength = 1000;

    public Guid Id { get; private set; }
    public DataExportStatus Status { get; private set; }
    public Guid? RequestedBy { get; private set; }
    public string? FileName { get; private set; }
    public byte[]? Content { get; private set; }
    public int? SizeBytes { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private DataExport() { }

    public static DataExport Request(Guid? requestedBy, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Status = DataExportStatus.Pending,
        RequestedBy = requestedBy,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public bool IsInProgress => Status is DataExportStatus.Pending or DataExportStatus.Running;

    public void MarkRunning(DateTimeOffset now)
    {
        Status = DataExportStatus.Running;
        UpdatedAt = now;
    }

    // Uygulama yeniden başlarken yarıda kalmış döküm baştan üretilir.
    public void Requeue(DateTimeOffset now)
    {
        Status = DataExportStatus.Pending;
        UpdatedAt = now;
    }

    public void Complete(string fileName, byte[] content, DateTimeOffset now)
    {
        Status = DataExportStatus.Completed;
        FileName = fileName;
        Content = content;
        SizeBytes = content.Length;
        ErrorMessage = null;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        Status = DataExportStatus.Failed;
        ErrorMessage = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        CompletedAt = now;
        UpdatedAt = now;
    }
}
