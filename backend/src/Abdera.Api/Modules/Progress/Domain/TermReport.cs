using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Progress.Domain;

public enum TermReportStatus
{
    Draft,
    Published,
}

// Dönem karnesi (issue #15): bir kurs kaydının (öğrenci + öğretmen + enstrüman) bir dönemdeki
// karnesi. Öğretmen yorumunu yazar ve yayınlar; yayınlanan karne veli portalında görünür.
// Kullanıcı kararı: yorumu öğretmen yazar, yapay zekâ taslağı YOK (L5 aynen geçerli).
//
// Yayınlanınca dönemin verileri (devam, eserler, yetenek puanları, pratik) SnapshotJson'a
// donar - aidattaki fiyat snapshot'ıyla aynı ilke: veliye verilmiş bir karne sonradan girilen
// bir yoklama ya da not yüzünden değişmez. Düzeltmek için yayından kaldırılıp yeniden yayınlanır.
public class TermReport
{
    public const int CommentMaxLength = 2000;

    public Guid Id { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid EnrollmentId { get; private set; }
    public Guid TeacherId { get; private set; }
    public string Term { get; private set; } = null!;
    public string? Comment { get; private set; }
    public TermReportStatus Status { get; private set; } = TermReportStatus.Draft;
    public string? SnapshotJson { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public Guid? PublishedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TermReport() { }

    public static TermReport Create(Guid studentId, Guid enrollmentId, Guid teacherId, ReportTerm term, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        StudentId = studentId,
        EnrollmentId = enrollmentId,
        TeacherId = teacherId,
        Term = term.Key,
        CreatedAt = now,
        UpdatedAt = now,
    };

    public void SaveComment(string? comment, DateTimeOffset now)
    {
        if (Status == TermReportStatus.Published)
            throw new ConflictException("Yayınlanmış karne değiştirilemez; önce yayından kaldır.");
        var trimmed = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (trimmed is { Length: > CommentMaxLength })
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["comment"] = [$"Yorum en fazla {CommentMaxLength} karakter olabilir."],
            });
        Comment = trimmed;
        UpdatedAt = now;
    }

    public void Publish(string snapshotJson, Guid publishedByUserId, DateTimeOffset now)
    {
        if (Status == TermReportStatus.Published)
            throw new ConflictException("Karne zaten yayında.");
        if (string.IsNullOrWhiteSpace(Comment))
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["comment"] = ["Yayınlamadan önce öğretmen yorumunu yaz."],
            });
        Status = TermReportStatus.Published;
        SnapshotJson = snapshotJson;
        PublishedAt = now;
        PublishedByUserId = publishedByUserId;
        UpdatedAt = now;
    }

    public void Unpublish(DateTimeOffset now)
    {
        if (Status != TermReportStatus.Published)
            throw new ConflictException("Karne yayında değil.");
        Status = TermReportStatus.Draft;
        SnapshotJson = null;
        PublishedAt = null;
        PublishedByUserId = null;
        UpdatedAt = now;
    }
}
