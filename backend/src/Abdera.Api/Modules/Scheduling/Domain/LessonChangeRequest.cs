using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Scheduling.Domain;

// docs/03-erd.md - Scheduling > lesson_change_requests. Her zaman bir "yeni saat önerisi"
// taşır (proposed_start_at/end_at NOT NULL) - düz iptal ayrı bir işlemdir (Lesson.Cancel).
public class LessonChangeRequest
{
    public Guid Id { get; private set; }
    public Guid LessonId { get; private set; }
    public Guid RequestedBy { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset ProposedStartAt { get; private set; }
    public DateTimeOffset ProposedEndAt { get; private set; }
    public LessonChangeRequestStatus Status { get; private set; } = LessonChangeRequestStatus.Pending;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    private LessonChangeRequest() { }

    public static LessonChangeRequest Create(
        Guid lessonId, Guid requestedBy, string? reason,
        DateTimeOffset proposedStartAt, DateTimeOffset proposedEndAt, DateTimeOffset now)
    {
        if (proposedEndAt <= proposedStartAt)
            throw new ArgumentException("Bitiş zamanı başlangıçtan sonra olmalı.", nameof(proposedEndAt));

        return new LessonChangeRequest
        {
            Id = Guid.NewGuid(),
            LessonId = lessonId,
            RequestedBy = requestedBy,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            ProposedStartAt = proposedStartAt,
            ProposedEndAt = proposedEndAt,
            Status = LessonChangeRequestStatus.Pending,
            CreatedAt = now,
        };
    }

    public void Approve(DateTimeOffset now)
    {
        EnsurePending();
        Status = LessonChangeRequestStatus.Approved;
        ResolvedAt = now;
    }

    public void Reject(DateTimeOffset now)
    {
        EnsurePending();
        Status = LessonChangeRequestStatus.Rejected;
        ResolvedAt = now;
    }

    private void EnsurePending()
    {
        if (Status != LessonChangeRequestStatus.Pending)
            throw new ConflictException($"Bu talep zaten '{Status}' durumunda, tekrar karara bağlanamaz.");
    }
}
