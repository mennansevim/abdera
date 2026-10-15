namespace Abdera.Platform.Api.Domain;

public enum RolloutStatus
{
    Running,
    Paused,
    Completed,
    Halted,
}

// Filo güncellemesi (§7): dalga 0 kanarya okullar, dalga 1 kalanların ~%10'u, dalga 2 geri kalanı.
// Bir dalgada hata olursa dağıtım durur (Halted); kanarya bekleme süresi dolmadan sonraki dalga başlamaz.
public class Rollout
{
    public Guid Id { get; private set; }
    public string Version { get; private set; } = null!;
    public int Wave { get; private set; }
    public RolloutStatus Status { get; private set; }
    public DateTimeOffset? WaveCompletedAt { get; private set; }
    public string? HaltReason { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public uint RowVersion { get; private set; }

    private Rollout() { }

    public static Rollout Start(string version, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        Version = version,
        Wave = 0,
        Status = RolloutStatus.Running,
        StartedAt = now,
    };

    public void CompleteWave(DateTimeOffset now) => WaveCompletedAt = now;

    public void NextWave(DateTimeOffset now)
    {
        Wave++;
        WaveCompletedAt = null;
    }

    public void Complete(DateTimeOffset now) { Status = RolloutStatus.Completed; FinishedAt = now; }
    public void Halt(string reason, DateTimeOffset now) { Status = RolloutStatus.Halted; HaltReason = reason; FinishedAt = now; }
}
