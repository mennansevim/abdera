using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Ops.Domain;

public enum BugReportKind
{
    Bug,
    Suggestion,
}

public enum BugReportStatus
{
    New,
    Triaged,
    Rejected,
    // Görev canlıya çıktı, bildirimi yapanın teyidi bekleniyor (W6).
    Resolved,
    // Bildirimi yapan "çalışıyor" dedi - süreç kapandı.
    Verified,
    // Bildirimi yapan "hâlâ hatalı" dedi; görev yeniden açılacak.
    Reopened,
    // Ne istendiği anlaşılamadı; soru (TriageNote) bildirimi yapana gösterilir, cevabı
    // gelince bildirim yeniden New olur (W7).
    NeedsInfo,
}

// Personelin panelden ilettiği hata/öneri bildirimi (docs/10-decisions.md W). Öğretmen gönderir
// ve unutur; takibi yönetici ile Claude yapar: her bildirim doğrulanır, gerçekse GitHub'da
// bug/enhancement issue'su açılır ve numarası buraya yazılır. Kayıt silinmez - reddedilen
// bildirim de "neden yapılmadı" sorusunun cevabıdır.
public class BugReport
{
    public const int MinDescriptionLength = 10;
    public const int MaxDescriptionLength = 4000;
    private const int MaxPagePathLength = 200;
    private const int MaxUserAgentLength = 500;
    private const int MaxAppVersionLength = 40;
    private const int MaxTriageNoteLength = 1000;

    public Guid Id { get; private set; }
    public BugReportKind Kind { get; private set; }
    public string PagePath { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string? UserAgent { get; private set; }
    public string? AppVersion { get; private set; }
    // Gönderen öğretmen kalıcı silinirse null'lanır; bildirimin kendisi okulun kaydıdır, kalır.
    public Guid? CreatedByUserId { get; private set; }
    public BugReportStatus Status { get; private set; }
    public int? GithubIssueNumber { get; private set; }
    public string? TriageNote { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    // Teyit sırasında "hâlâ hatalı" diyenin yazdığı açıklama.
    public string? FeedbackNote { get; private set; }
    public DateTimeOffset? FeedbackAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private BugReport() { }

    public static BugReport Create(
        Guid createdByUserId, BugReportKind kind, string pagePath, string description,
        string? userAgent, string? appVersion, DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Geçersiz bildirim türü.", nameof(kind));
        if (string.IsNullOrWhiteSpace(pagePath)) throw new ArgumentException("Hangi sayfada olduğunu seçin.", nameof(pagePath));
        var trimmedDescription = description?.Trim() ?? "";
        if (trimmedDescription.Length < MinDescriptionLength)
            throw new ArgumentException($"Açıklama en az {MinDescriptionLength} karakter olmalı.", nameof(description));
        if (trimmedDescription.Length > MaxDescriptionLength)
            throw new ArgumentException($"Açıklama en fazla {MaxDescriptionLength} karakter olabilir.", nameof(description));

        return new BugReport
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            PagePath = Truncate(pagePath.Trim(), MaxPagePathLength)!,
            Description = trimmedDescription,
            UserAgent = Truncate(userAgent?.Trim(), MaxUserAgentLength),
            AppVersion = Truncate(appVersion?.Trim(), MaxAppVersionLength),
            CreatedByUserId = createdByUserId,
            Status = BugReportStatus.New,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Triage(BugReportStatus status, int? githubIssueNumber, string? triageNote, DateTimeOffset now)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentException("Geçersiz durum.", nameof(status));
        // Teyit akışının durumları kendi yöntemleriyle değişir; burası yalnızca ilk triage.
        if (status is BugReportStatus.Resolved or BugReportStatus.Verified or BugReportStatus.Reopened)
            throw new ArgumentException("Bu durum yalnızca çözüldü/teyit akışıyla verilir.", nameof(status));
        if (status == BugReportStatus.Triaged && githubIssueNumber is null or <= 0)
            throw new ArgumentException("Göreve dönüşen bildirim için GitHub issue numarası gerekli.", nameof(githubIssueNumber));
        var note = string.IsNullOrWhiteSpace(triageNote) ? null : triageNote.Trim();
        if (note?.Length > MaxTriageNoteLength)
            throw new ArgumentException($"Not en fazla {MaxTriageNoteLength} karakter olabilir.", nameof(triageNote));
        if (status == BugReportStatus.NeedsInfo && note is null)
            throw new ArgumentException("Detay isterken bildirimi yapana ne sorulduğunu yazın.", nameof(triageNote));

        Status = status;
        GithubIssueNumber = status == BugReportStatus.Triaged ? githubIssueNumber : null;
        TriageNote = note;
        UpdatedAt = now;
    }

    // Görevin canlıya çıktığını işaretler (W6). Göreve dönüşmemiş bildirim çözülemez; issue
    // numarası korunur ki teyit "hangi görev" sorusuna cevap versin.
    public void MarkResolved(DateTimeOffset now)
    {
        if (Status is not (BugReportStatus.Triaged or BugReportStatus.Reopened) || GithubIssueNumber is null)
            throw new ConflictException("Yalnızca göreve dönüşmüş bildirim çözüldü olarak işaretlenebilir.");
        Status = BugReportStatus.Resolved;
        ResolvedAt = now;
        UpdatedAt = now;
    }

    // Bildirimi yapanın teyidi: çalışıyorsa Verified, değilse Reopened + açıklama.
    public void Confirm(bool works, string? note, DateTimeOffset now)
    {
        if (Status != BugReportStatus.Resolved)
            throw new ConflictException("Yalnızca çözüldü olarak işaretlenen bildirim teyit edilebilir.");
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed?.Length > MaxTriageNoteLength)
            throw new ArgumentException($"Açıklama en fazla {MaxTriageNoteLength} karakter olabilir.", nameof(note));
        Status = works ? BugReportStatus.Verified : BugReportStatus.Reopened;
        FeedbackNote = works ? null : trimmed;
        FeedbackAt = now;
        UpdatedAt = now;
    }


    // Detay istenen bildirime, bildirimi yapanın cevabı (W7). Cevap açıklamanın sonuna tarihli
    // eklenir - bir sonraki inceleme ilk metinle cevabı birlikte görür. Soru da kayıtta kalır.
    public void ProvideDetails(string details, DateTimeOffset now, TimeZoneInfo schoolTimeZone)
    {
        if (Status != BugReportStatus.NeedsInfo)
            throw new ConflictException("Bu bildirim için detay istenmedi.");
        var trimmed = details?.Trim() ?? "";
        if (trimmed.Length < MinDescriptionLength)
            throw new ArgumentException($"Açıklama en az {MinDescriptionLength} karakter olmalı.", nameof(details));
        var stamp = TimeZoneInfo.ConvertTime(now, schoolTimeZone).ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var combined = $"{Description}\n\n— Soru: {TriageNote}\n— Ek bilgi ({stamp}): {trimmed}";
        if (combined.Length > MaxDescriptionLength)
            throw new ArgumentException($"Açıklamayla birlikte en fazla {MaxDescriptionLength} karakter olabilir.", nameof(details));
        Description = combined;
        Status = BugReportStatus.New;
        TriageNote = null;
        UpdatedAt = now;
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];
}
