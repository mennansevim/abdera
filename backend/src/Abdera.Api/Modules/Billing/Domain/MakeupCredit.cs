using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Billing.Domain;

public enum MakeupCreditEarnedReason
{
    GuardianCancelled24H,
    SchoolCancelled,
}

public enum MakeupCreditStatus
{
    Available,
    Used,
    Expired,
}

// docs/03-erd.md - Billing > makeup_credits. Billing modülünün Phase 3'te açılan tek dilimi -
// FeePlan/Receivable/Payment Phase 4'te (Pricing ile birlikte) geliyor. docs/10-decisions.md A2:
// dersten ≥24 saat önce iptal edilirse doğar; habersiz gelmeme (ABSENT) kredi doğurmaz.
public class MakeupCredit
{
    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid SourceLessonId { get; private set; }
    public MakeupCreditEarnedReason EarnedReason { get; private set; }
    public DateTimeOffset EarnedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public Guid? UsedLessonId { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }
    public MakeupCreditStatus Status { get; private set; } = MakeupCreditStatus.Available;

    private MakeupCredit() { }

    public static MakeupCredit Earn(
        Guid studentId, Guid sourceLessonId, MakeupCreditEarnedReason reason,
        DateTimeOffset now, int validDays) => new()
    {
        Id = Guid.NewGuid(),
        StudentId = studentId,
        SourceLessonId = sourceLessonId,
        EarnedReason = reason,
        EarnedAt = now,
        ExpiresAt = now.AddDays(validDays),
        Status = MakeupCreditStatus.Available,
    };

    // docs/05-state-models.md: AVAILABLE -> USED, "telafi dersi planlandı".
    public void Use(Guid usedLessonId, DateTimeOffset now)
    {
        if (Status != MakeupCreditStatus.Available)
            throw new ConflictException($"Bu telafi kredisi '{Status}' durumunda, kullanılamaz.");
        if (now > ExpiresAt)
            throw new ConflictException("Bu telafi kredisinin süresi dolmuş.");

        Status = MakeupCreditStatus.Used;
        UsedLessonId = usedLessonId;
        UsedAt = now;
    }

    public void ExpireIfPastDue(DateTimeOffset now)
    {
        if (Status == MakeupCreditStatus.Available && now > ExpiresAt)
        {
            Status = MakeupCreditStatus.Expired;
        }
    }
}
