using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Show.Domain;

public enum ShowItemKind
{
    // Bir öğrencinin bir eseri. Programın asıl birimi.
    Performance,
    // Ara. Sahne akışında sırası vardır ama sahnede kimse yoktur.
    Intermission,
    // Açılış konuşması, teşekkür, sertifika töreni gibi öğrenciye bağlı olmayan sıra.
    Announcement,
}

// Programın tek bir sırası. Kasıtlı olarak DÜZ bir liste: ayrı bir "bölüm" tablosu yerine
// grup adı satırın kendisinde taşınır (GroupName). Gerekçe - bir okul resitalinde bölüm,
// sıralamayı değiştiren bir yapı değil yalnızca bir başlık; ayrı tablo, sürükle-bırak
// sıralamayı iki boyutlu (bölüm içi + bölümler arası) hâle getirip hiçbir karşılığı
// olmayan bir karmaşıklık eklerdi.
//
// Bir öğrencinin BİRDEN FAZLA eseri varsa her eser ayrı bir satırdır: sahne ekranındaki
// "şu an çalınan eser" işaretçisi eser bazında ilerlemek zorunda ("şu an çalacağı eser
// büyük punto ile" - kullanıcı isteği). Öğrencinin tüm eserleri aynı StudentId ile
// bulunup birlikte gösterilir.
public class ShowItem
{
    private const int MaxTextLength = 200;

    public Guid Id { get; private set; }
    public Guid ShowEventId { get; private set; }
    // Sıra numarası. 0'dan başlar, yeniden sıralamada topluca yazılır.
    public int Position { get; private set; }
    public string? GroupName { get; private set; }
    public ShowItemKind Kind { get; private set; }
    public Guid? StudentId { get; private set; }
    public Guid? InstrumentId { get; private set; }
    public Guid? TeacherId { get; private set; }
    public string? PieceTitle { get; private set; }
    public string? Composer { get; private set; }
    // Programın toplam süresini ve "ne kadar geciktik" hesabını mümkün kılar.
    public int? DurationMinutes { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ShowItem() { }

    public static ShowItem Create(
        Guid showEventId, int position, ShowItemKind kind, string? groupName,
        Guid? studentId, Guid? instrumentId, Guid? teacherId,
        string? pieceTitle, string? composer, int? durationMinutes, string? note, DateTimeOffset now)
    {
        var item = new ShowItem
        {
            Id = Guid.NewGuid(),
            ShowEventId = showEventId,
            Position = position,
            CreatedAt = now,
        };

        item.Apply(kind, groupName, studentId, instrumentId, teacherId, pieceTitle, composer, durationMinutes, note, now);
        return item;
    }

    public void Apply(
        ShowItemKind kind, string? groupName, Guid? studentId, Guid? instrumentId, Guid? teacherId,
        string? pieceTitle, string? composer, int? durationMinutes, string? note, DateTimeOffset now)
    {
        if (kind == ShowItemKind.Performance)
        {
            if (studentId is null)
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    ["studentId"] = ["Sahne sırası için öğrenci seçilmeli."],
                });
            if (string.IsNullOrWhiteSpace(pieceTitle))
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    ["pieceTitle"] = ["Eser adı boş olamaz."],
                });
        }

        if (durationMinutes is < 1 or > 120)
        {
            if (durationMinutes is not null)
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    ["durationMinutes"] = ["Süre 1 ile 120 dakika arasında olmalı."],
                });
        }

        Kind = kind;
        GroupName = Trim(groupName);
        // Ara ve duyuru satırlarında öğrenci/enstrüman/eser taşımak, sahne ekranında
        // "kim çalıyor" sorusuna yanlış cevap verirdi - bilinçli olarak temizleniyor.
        StudentId = kind == ShowItemKind.Performance ? studentId : null;
        InstrumentId = kind == ShowItemKind.Performance ? instrumentId : null;
        TeacherId = kind == ShowItemKind.Performance ? teacherId : null;
        PieceTitle = kind == ShowItemKind.Performance ? Trim(pieceTitle) : Trim(pieceTitle ?? note);
        Composer = kind == ShowItemKind.Performance ? Trim(composer) : null;
        DurationMinutes = durationMinutes;
        Note = Trim(note);
        UpdatedAt = now;
    }

    public void MoveTo(int position, DateTimeOffset now)
    {
        if (position < 0) throw new ArgumentOutOfRangeException(nameof(position), "Sıra numarası negatif olamaz.");
        Position = position;
        UpdatedAt = now;
    }

    private static string? Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > MaxTextLength ? trimmed[..MaxTextLength] : trimmed;
    }
}
