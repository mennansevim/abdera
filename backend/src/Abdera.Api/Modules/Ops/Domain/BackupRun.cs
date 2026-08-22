namespace Abdera.Api.Modules.Ops.Domain;

// Faz 4 (docs/15-product-phases.md): her yedekleme denemesinin kaydı. Finansal/audit
// kayıtlar gibi silinmez - başarısız denemeler de saklanır, "neden yedek alınamadı"
// sorusuna panelden cevap verilebilsin diye.
public enum BackupRunStatus
{
    Running,
    Succeeded,
    Failed,
}

public class BackupRun
{
    public Guid Id { get; private set; }
    public BackupRunStatus Status { get; private set; }
    public bool TriggeredManually { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public long? SizeBytes { get; private set; }
    public string? RemotePath { get; private set; }
    public string? ErrorMessage { get; private set; }

    private BackupRun() { }

    public static BackupRun Start(bool triggeredManually, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Status = BackupRunStatus.Running,
        TriggeredManually = triggeredManually,
        StartedAt = now,
    };

    public void MarkSucceeded(string remotePath, long sizeBytes, DateTimeOffset now)
    {
        Status = BackupRunStatus.Succeeded;
        RemotePath = remotePath;
        SizeBytes = sizeBytes;
        CompletedAt = now;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Status = BackupRunStatus.Failed;
        ErrorMessage = error;
        CompletedAt = now;
    }
}
