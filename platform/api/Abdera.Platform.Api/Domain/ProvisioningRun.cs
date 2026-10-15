namespace Abdera.Platform.Api.Domain;

public enum RunKind
{
    Create,
    Upgrade,
    Suspend,
    Resume,
    Destroy,
    Undestroy,
    Purge,
    Cancel,
    Reconfigure,
    Drill,
    Adopt,
}

public enum RunStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Cancelled,
}

public enum StepStatus
{
    Running,
    Succeeded,
    Failed,
}

// Bir okul üzerinde yürütülen işlem (§3.2 provisioning_runs). Adımlar sırayla yürür; her adım
// idempotenttir ve ajana kalıcı bir işlem kimliğiyle (--op) gider, böylece bağlantı koparsa yeniden
// deneme aynı sonucu okur (§16.2). Çalışan işin süreli sahipliği (lease) vardır: platform süreci
// ölürse süre dolunca başka bir tur devralır.
public class ProvisioningRun
{
    public Guid Id { get; private set; }
    public Guid SchoolId { get; private set; }
    public RunKind Kind { get; private set; }
    public RunStatus Status { get; private set; }
    public int CurrentStep { get; private set; }
    public string? TargetVersion { get; private set; }
    public Guid? RolloutId { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public uint RowVersion { get; private set; }

    private ProvisioningRun() { }

    public static ProvisioningRun Create(Guid schoolId, RunKind kind, DateTimeOffset now, string? targetVersion = null, Guid? rolloutId = null) => new()
    {
        Id = Guid.NewGuid(),
        SchoolId = schoolId,
        Kind = kind,
        Status = RunStatus.Pending,
        TargetVersion = targetVersion,
        RolloutId = rolloutId,
        CreatedAt = now,
    };

    public bool IsOpen => Status is RunStatus.Pending or RunStatus.Running;

    // Ajana giden işlem kimliği: run + adım + deneme. Çöken bir turun yarıda kalan denemesi devralınınca
    // aynı kimlik gider ve ajan kaydettiği sonucu döndürür (komut iki kez çalışmaz). Başarısız adımın
    // "yeniden dene"si yeni bir denemedir: yeni kimlik, komut gerçekten yeniden çalışır.
    public string OperationId(int step, int attempt) => $"{Id:N}-{step}-{attempt}";

    public void Claim(DateTimeOffset now, TimeSpan lease)
    {
        Status = RunStatus.Running;
        StartedAt ??= now;
        LeaseUntil = now + lease;
    }

    public void Advance(int nextStep, DateTimeOffset now, TimeSpan lease)
    {
        CurrentStep = nextStep;
        LeaseUntil = now + lease;
    }

    public void Succeed(DateTimeOffset now) { Status = RunStatus.Succeeded; FinishedAt = now; LeaseUntil = null; Error = null; }

    public void Fail(string error, DateTimeOffset now)
    {
        Status = RunStatus.Failed;
        Error = error.Length > 2000 ? error[..2000] : error;
        FinishedAt = now;
        LeaseUntil = null;
    }

    // "Yeniden dene" kaldığı adımdan devam eder (§6).
    public void Retry(DateTimeOffset now)
    {
        if (Status != RunStatus.Failed) throw new ConflictException("Yalnızca başarısız işlem yeniden denenebilir.");
        Status = RunStatus.Pending;
        Error = null;
        FinishedAt = null;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is RunStatus.Succeeded or RunStatus.Cancelled) return;
        Status = RunStatus.Cancelled;
        FinishedAt = now;
        LeaseUntil = null;
    }
}

public class ProvisioningStep
{
    public Guid Id { get; private set; }
    public Guid RunId { get; private set; }
    public int Step { get; private set; }
    public string Name { get; private set; } = null!;
    public StepStatus Status { get; private set; }
    public int Attempt { get; private set; }
    public string? Log { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    private ProvisioningStep() { }

    public static ProvisioningStep Start(Guid runId, int step, string name, int attempt, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        RunId = runId,
        Step = step,
        Name = name,
        Status = StepStatus.Running,
        Attempt = attempt,
        StartedAt = now,
    };

    public void Finish(bool success, string? log, DateTimeOffset now)
    {
        Status = success ? StepStatus.Succeeded : StepStatus.Failed;
        Log = log is { Length: > 4000 } ? log[..4000] : log;
        FinishedAt = now;
    }
}
