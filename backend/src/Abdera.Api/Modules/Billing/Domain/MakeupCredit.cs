using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Billing.Domain;

public enum MakeupCreditEarnedReason
{
    GuardianCancelled24H,
    SchoolCancelled,
    // Yoklamada "Mazeretli" işaretlenen ders (docs/10-decisions.md A2 altındaki satır).
    Excused,
}

public enum MakeupCreditStatus
{
    Available,
    Used,
    Expired,
    // Hakkı doğuran durum geri alındı (ör. "Mazeretli" yoklaması "Geldi"ye düzeltildi).
    // Satır silinmez; neden artık kullanılamadığı kayıtta kalır.
    Revoked,
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
        DateTimeOffset now, int validDays) =>
        Earn(studentId, sourceLessonId, reason, now, expiresAt: now.AddDays(validDays));

    public static MakeupCredit Earn(
        Guid studentId, Guid sourceLessonId, MakeupCreditEarnedReason reason,
        DateTimeOffset now, DateTimeOffset expiresAt) => new()
    {
        Id = Guid.NewGuid(),
        StudentId = studentId,
        SourceLessonId = sourceLessonId,
        EarnedReason = reason,
        EarnedAt = now,
        ExpiresAt = expiresAt,
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

    // Yalnızca henüz kullanılmamış hak geri alınır: telafi dersi zaten planlandıysa o ders
    // takvimde durur, hak "Used" kalır.
    public bool Revoke()
    {
        if (Status != MakeupCreditStatus.Available) return false;
        Status = MakeupCreditStatus.Revoked;
        return true;
    }

    public void ExpireIfPastDue(DateTimeOffset now)
    {
        if (Status == MakeupCreditStatus.Available && now > ExpiresAt)
        {
            Status = MakeupCreditStatus.Expired;
        }
    }
}
