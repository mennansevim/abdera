using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Messaging.Domain;

// docs/03-erd.md - Messaging > notification_jobs. docs/05-state-models.md durum makinesi
// burada uygulanır. UNIQUE (type, reference_type, reference_id) veritabanı kısıtı A5'in
// idempotency garantisi - aynı ders için ikinci bir hatırlatma job'ı DB seviyesinde engellenir.
public class NotificationJob
{
    public Guid Id { get; private set; }
    public NotificationJobType Type { get; private set; }
    public string RecipientPhoneNumber { get; private set; } = null!;
    public string ReferenceType { get; private set; } = null!;
    public Guid ReferenceId { get; private set; }
    public DateTimeOffset ScheduledAt { get; private set; }
    public NotificationJobStatus Status { get; private set; } = NotificationJobStatus.Pending;
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private NotificationJob() { }

    public static NotificationJob Create(
        NotificationJobType type, string recipientPhoneNumber, string referenceType, Guid referenceId,
        DateTimeOffset scheduledAt, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(recipientPhoneNumber))
            throw new ArgumentException("Alıcı telefon numarası boş olamaz.", nameof(recipientPhoneNumber));

        return new NotificationJob
        {
            Id = Guid.NewGuid(),
            Type = type,
            RecipientPhoneNumber = recipientPhoneNumber,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ScheduledAt = scheduledAt,
            Status = NotificationJobStatus.Pending,
            AttemptCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Claim(DateTimeOffset now)
    {
        if (Status != NotificationJobStatus.Pending)
            throw new ConflictException($"'{Status}' durumundaki bir job işlenmek üzere alınamaz.");

        Status = NotificationJobStatus.Processing;
        AttemptCount++;
        UpdatedAt = now;
    }

    public void MarkSent(DateTimeOffset now)
    {
        Status = NotificationJobStatus.Sent;
        SentAt = now;
        UpdatedAt = now;
    }

    // docs/05-state-models.md: "FAILED -> PENDING geçişi attempt_count < MaxAttempts olduğu
    // sürece otomatik; limit aşılınca job FAILED kalır ve yönetici panelinde 'yeniden dene'
    // ile elle tetiklenir."
    public void MarkFailed(string error, int maxAttempts, DateTimeOffset now)
    {
        LastError = error;
        UpdatedAt = now;
        Status = AttemptCount < maxAttempts ? NotificationJobStatus.Pending : NotificationJobStatus.Failed;
    }

    // docs/10-decisions.md A4: ders değişince/iptal olunca bekleyen job iptal edilir.
    public void Cancel(DateTimeOffset now)
    {
        if (Status is NotificationJobStatus.Sent or NotificationJobStatus.Cancelled) return;

        Status = NotificationJobStatus.Cancelled;
        UpdatedAt = now;
    }

    // docs/06-whatsapp.md A6: sessiz saat dışındaki job, bir sonraki pencere başına ötelenir.
    public void Reschedule(DateTimeOffset newScheduledAt, DateTimeOffset now)
    {
        ScheduledAt = newScheduledAt;
        UpdatedAt = now;
    }

    // Master prompt: "failed jobs must remain visible" - Admin, deneme limitine bakmaksızın
    // elle yeniden kuyruğa alabilir.
    public void RetryManually(DateTimeOffset now)
    {
        if (Status != NotificationJobStatus.Failed)
            throw new ConflictException($"Yalnızca '{NotificationJobStatus.Failed}' durumundaki bir job yeniden denenebilir.");

        Status = NotificationJobStatus.Pending;
        UpdatedAt = now;
    }
}
