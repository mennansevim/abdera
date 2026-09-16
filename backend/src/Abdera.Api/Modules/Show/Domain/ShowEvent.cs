using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Show.Domain;

public enum ShowEventStatus
{
    Draft,
    Live,
    Completed,
}

// Yıl sonu gösterisi. Tek bir organizasyon: sıralı bir program ve canlı akış işaretçisi.
//
// Benzer araçların (StageManager.tech, Rundown Studio, VI-Stage) ortak deseni: program
// önceden kurulur, gösteri gecesi "performance mode"a geçilir ve TEK bir işaretçi tüm
// ekranlarda senkron ilerler. İşaretçiyi (CurrentItemId) veritabanında tutmamızın sebebi
// tam da bu: sahne ekranı, kulis tableti ve yönetici ekranı aynı anı görmek zorunda -
// tarayıcı state'i olsaydı her ekran kendi sırasını gösterirdi.
public class ShowEvent
{
    private const int MaxTitleLength = 150;

    public Guid Id { get; private set; }
    public string Title { get; private set; } = null!;
    public string? VenueName { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public ShowEventStatus Status { get; private set; } = ShowEventStatus.Draft;
    // Canlı akışın nerede olduğu. Null + Live = gösteri başladı ama henüz ilk sıraya
    // geçilmedi (sahne boş, açılış konuşması vb.).
    public Guid? CurrentItemId { get; private set; }
    // Geçen süreyi gösterebilmek için - kulis ekranı "programın 12 dakika gerisindeyiz"
    // diyebilsin (run-of-show araçlarının standart göstergesi).
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ShowEvent() { }

    public static ShowEvent Create(string title, string? venueName, DateTimeOffset startsAt, DateTimeOffset now)
    {
        ValidateTitle(title);

        return new ShowEvent
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            VenueName = string.IsNullOrWhiteSpace(venueName) ? null : venueName.Trim(),
            StartsAt = startsAt,
            Status = ShowEventStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void Update(string title, string? venueName, DateTimeOffset startsAt, DateTimeOffset now)
    {
        ValidateTitle(title);

        Title = title.Trim();
        VenueName = string.IsNullOrWhiteSpace(venueName) ? null : venueName.Trim();
        StartsAt = startsAt;
        UpdatedAt = now;
    }

    public void Start(DateTimeOffset now)
    {
        if (Status == ShowEventStatus.Completed)
            throw new ConflictException("Tamamlanmış bir gösteri yeniden başlatılamaz.");
        if (Status == ShowEventStatus.Live) return;

        Status = ShowEventStatus.Live;
        StartedAt = now;
        EndedAt = null;
        UpdatedAt = now;
    }

    // Sahne işaretçisini taşır. Null göndermek "sahne boş" demektir (ara, açılış).
    public void MoveTo(Guid? itemId, DateTimeOffset now)
    {
        if (Status != ShowEventStatus.Live)
            throw new ConflictException("Sıra değiştirmek için önce gösteriyi başlatın.");

        CurrentItemId = itemId;
        UpdatedAt = now;
    }

    public void Finish(DateTimeOffset now)
    {
        Status = ShowEventStatus.Completed;
        CurrentItemId = null;
        EndedAt = now;
        UpdatedAt = now;
    }

    // Gösteri gecesi yanlışlıkla "bitir"e basılırsa geri dönülebilsin - kalıcı bir kayıp
    // değil, yalnızca bir durum.
    public void ReopenAsDraft(DateTimeOffset now)
    {
        Status = ShowEventStatus.Draft;
        CurrentItemId = null;
        StartedAt = null;
        EndedAt = null;
        UpdatedAt = now;
    }

    private static void ValidateTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Gösteri adı boş olamaz.", nameof(title));
        if (title.Trim().Length > MaxTitleLength)
            throw new ArgumentException($"Gösteri adı en fazla {MaxTitleLength} karakter olabilir.", nameof(title));
    }
}
