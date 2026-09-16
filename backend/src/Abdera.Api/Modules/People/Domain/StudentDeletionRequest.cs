using Abdera.Api.Shared;

namespace Abdera.Api.Modules.People.Domain;

public enum StudentDeletionRequestStatus
{
    Pending,
    Approved,
    Rejected,
}

// Öğretmenin "bu öğrenci silinsin" talebi (docs/10-decisions.md J2). Öğretmen kendi
// öğrencisini ekleyip düzenleyebiliyor ama SİLEMİYOR - silme geri alınamaz ve öğrencinin
// mali/ders geçmişini de götürür, bu yüzden kararı yöneticiye bırakan bir talep adımı var.
//
// Scheduling'deki LessonChangeRequest ile aynı desen: Pending -> Approved | Rejected, ve
// karara bağlanmış bir talep tekrar karara bağlanamaz.
public class StudentDeletionRequest
{
    private const int MaxReasonLength = 500;

    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public string Reason { get; private set; } = null!;
    public StudentDeletionRequestStatus Status { get; private set; } = StudentDeletionRequestStatus.Pending;
    public Guid? DecidedBy { get; private set; }
    public string? DecisionNote { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    private StudentDeletionRequest() { }

    public static StudentDeletionRequest Create(Guid studentId, Guid requestedBy, string reason, DateTimeOffset now)
    {
        // Gerekçe zorunlu: yöneticinin onaylayıp onaylamayacağına karar verebilmesi için
        // "neden" bilgisi şart - gerekçesiz bir silme talebi karar verilemez bir taleptir.
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["reason"] = ["Silme gerekçesi zorunlu."],
            });

        var trimmed = reason.Trim();
        return new StudentDeletionRequest
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            RequestedBy = requestedBy,
            Reason = trimmed.Length > MaxReasonLength ? trimmed[..MaxReasonLength] : trimmed,
            Status = StudentDeletionRequestStatus.Pending,
            CreatedAt = now,
        };
    }

    public void Approve(Guid decidedBy, string? note, DateTimeOffset now)
    {
        EnsurePending();
        Status = StudentDeletionRequestStatus.Approved;
        Decide(decidedBy, note, now);
    }

    public void Reject(Guid decidedBy, string? note, DateTimeOffset now)
    {
        EnsurePending();
        Status = StudentDeletionRequestStatus.Rejected;
        Decide(decidedBy, note, now);
    }

    private void Decide(Guid decidedBy, string? note, DateTimeOffset now)
    {
        DecidedBy = decidedBy;
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ResolvedAt = now;
    }

    private void EnsurePending()
    {
        if (Status != StudentDeletionRequestStatus.Pending)
            throw new ConflictException($"Bu talep zaten '{Status}' durumunda, tekrar karara bağlanamaz.");
    }
}
