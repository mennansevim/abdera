namespace Abdera.Api.Modules.Progress.Domain;

public enum RepertoireStatus
{
    Learning,
    Polishing,
    PerformanceReady,
    Archived,
}

// docs/03-erd.md - Progress > lesson_notes. Ham öğretmen notu, repertuvar alanları ve
// ayrı onay yaşam döngülü veli yorumu bu aggregate üzerinde birlikte tutulur.
public class LessonNote
{
    public Guid Id { get; private set; }
    public Guid LessonId { get; private set; }
    public Guid TeacherId { get; private set; }
    public string? Practiced { get; private set; }
    public string? Note { get; private set; }
    public string? Homework { get; private set; }
    public string? NextGoal { get; private set; }
    public string? PieceTitle { get; private set; }
    public int? PieceDifficulty { get; private set; }
    public string? PieceComposer { get; private set; }
    public RepertoireStatus? PieceStatus { get; private set; }
    public DateOnly? PieceTargetDate { get; private set; }
    public string? PieceResourceUrl { get; private set; }
    public bool PieceResourceVisibleToGuardian { get; private set; }
    public string? ParentComment { get; private set; }
    public DateTimeOffset? ParentCommentApprovedAt { get; private set; }
    public Guid? ParentCommentApprovedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private LessonNote() { }

    public static LessonNote Create(
        Guid lessonId, Guid teacherId, string? practiced, string? note, string? homework, string? nextGoal,
        string? pieceTitle, int? pieceDifficulty, DateTimeOffset now,
        string? pieceComposer = null,
        RepertoireStatus? pieceStatus = null,
        DateOnly? pieceTargetDate = null,
        string? pieceResourceUrl = null,
        bool pieceResourceVisibleToGuardian = false)
    {
        if (pieceDifficulty is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(pieceDifficulty), "Eser zorluğu 1 ile 5 arasında olmalı.");
        if (!string.IsNullOrWhiteSpace(pieceResourceUrl) &&
            (!Uri.TryCreate(pieceResourceUrl, UriKind.Absolute, out var resourceUri) ||
             resourceUri.Scheme is not ("https" or "http")))
            throw new ArgumentException("Eser bağlantısı geçerli bir http/https adresi olmalı.", nameof(pieceResourceUrl));

        return new LessonNote
        {
            Id = Guid.NewGuid(),
            LessonId = lessonId,
            TeacherId = teacherId,
            Practiced = Trim(practiced),
            Note = Trim(note),
            Homework = Trim(homework),
            NextGoal = Trim(nextGoal),
            PieceTitle = Trim(pieceTitle),
            PieceDifficulty = pieceDifficulty,
            PieceComposer = Trim(pieceComposer),
            PieceStatus = pieceStatus,
            PieceTargetDate = pieceTargetDate,
            PieceResourceUrl = Trim(pieceResourceUrl),
            PieceResourceVisibleToGuardian = pieceResourceVisibleToGuardian,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void SetParentCommentDraft(string parentComment, DateTimeOffset now)
    {
        ParentComment = Trim(parentComment)
            ?? throw new ArgumentException("Veli yorumu boş olamaz.", nameof(parentComment));
        ParentCommentApprovedAt = null;
        ParentCommentApprovedBy = null;
        UpdatedAt = now;
    }

    public void ApproveParentComment(Guid teacherId, DateTimeOffset now)
    {
        if (teacherId != TeacherId)
            throw new ArgumentException("Yorumu yalnızca notu yazan öğretmen onaylayabilir.", nameof(teacherId));
        if (string.IsNullOrWhiteSpace(ParentComment))
            throw new ArgumentException("Onaylanacak veli yorumu bulunamadı.");

        ParentCommentApprovedAt = now;
        ParentCommentApprovedBy = teacherId;
        UpdatedAt = now;
    }

    public void RevokeParentComment(Guid teacherId, DateTimeOffset now)
    {
        if (teacherId != TeacherId)
            throw new ArgumentException("Yorumu yalnızca notu yazan öğretmen geri çekebilir.", nameof(teacherId));

        ParentCommentApprovedAt = null;
        ParentCommentApprovedBy = null;
        UpdatedAt = now;
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
