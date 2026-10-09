using Abdera.Api.Modules.People.Domain;

namespace Abdera.Api.Modules.Profitability.Domain;

// Kârlılık ekranındaki "Fikirlerim" (docs/10-decisions.md V1). Yöneticinin aklındaki işletme
// fikri - yeni öğretmen, grup dersi, yeni branş, teşvik indirimi, tarife değişikliği ya da
// serbest bir fikir - birkaç parametreyle kaydedilir; etkisini GrowthIdeaEvaluator okulun
// güncel verisiyle her açılışta yeniden hesaplar. Fikir hiçbir kaydı değiştirmez: ne tarife
// açar ne öğrenci ekler, yalnızca "yaparsam ne olur" sorusunu saklar.
public enum GrowthIdeaKind
{
    NewTeacher,
    GroupClass,
    NewBranch,
    Incentive,
    PriceChange,
    Custom,
}

public enum GrowthIdeaStatus
{
    Idea,
    Trying,
    Applied,
    Dropped,
}

// Parametreler türe göre anlam kazanır; kullanılmayanlar null kalır. Ayrı tablo/jsonb yerine
// düz kolonlar: alan sayısı küçük ve sabit, değerlendirici de tipli okuyabilsin.
public record GrowthIdeaParameters(
    Guid? InstrumentId,
    string? BranchName,
    CourseKind? CourseKind,
    int? Students,
    decimal? TeacherRatePerLesson,
    decimal? DiscountPercent,
    int? DiscountMonths,
    int? AlreadyComingPercent,
    decimal? PriceChangePercent,
    int? LostStudents,
    decimal? MonthlyAmount,
    decimal? OneTimeCost)
{
    public static readonly GrowthIdeaParameters None = new(null, null, null, null, null, null, null, null, null, null, null, null);
}

public class GrowthIdea
{
    public const int TitleMaxLength = 120;
    public const int NoteMaxLength = 1000;
    public const int BranchNameMaxLength = 60;
    public const int MaxStudents = 200;

    public Guid Id { get; private set; }
    public GrowthIdeaKind Kind { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Note { get; private set; }
    public GrowthIdeaStatus Status { get; private set; } = GrowthIdeaStatus.Idea;
    public Guid? InstrumentId { get; private set; }
    public string? BranchName { get; private set; }
    public CourseKind? CourseKind { get; private set; }
    public int? Students { get; private set; }
    public decimal? TeacherRatePerLesson { get; private set; }
    public decimal? DiscountPercent { get; private set; }
    public int? DiscountMonths { get; private set; }
    public int? AlreadyComingPercent { get; private set; }
    public decimal? PriceChangePercent { get; private set; }
    public int? LostStudents { get; private set; }
    public decimal? MonthlyAmount { get; private set; }
    public decimal? OneTimeCost { get; private set; }
    // Tutar parametrelerinin (ücret, aylık etki, tek seferlik maliyet) para birimi.
    public string Currency { get; private set; } = "TRY";
    public Guid? CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private GrowthIdea() { }

    public GrowthIdeaParameters Parameters => new(
        InstrumentId, BranchName, CourseKind, Students, TeacherRatePerLesson, DiscountPercent, DiscountMonths,
        AlreadyComingPercent, PriceChangePercent, LostStudents, MonthlyAmount, OneTimeCost);

    public static GrowthIdea Create(
        GrowthIdeaKind kind, string title, string? note, GrowthIdeaParameters parameters, Guid? actorId, DateTimeOffset now)
    {
        var idea = new GrowthIdea { Id = Guid.NewGuid(), CreatedBy = actorId, CreatedAt = now };
        idea.Apply(kind, title, note, parameters, now);
        return idea;
    }

    public void Update(GrowthIdeaKind kind, string title, string? note, GrowthIdeaParameters parameters, DateTimeOffset now) =>
        Apply(kind, title, note, parameters, now);

    public void SetStatus(GrowthIdeaStatus status, DateTimeOffset now)
    {
        Status = status;
        UpdatedAt = now;
    }

    private void Apply(GrowthIdeaKind kind, string title, string? note, GrowthIdeaParameters parameters, DateTimeOffset now)
    {
        // Endpoint önce Validate ile alan bazlı 400 döner; buraya geçersiz değer gelmesi bir
        // programlama hatasıdır, yine de invariant entity'de korunur.
        var errors = Validate(kind, title, note, parameters);
        if (errors.Count > 0) throw new ArgumentException(errors.First().Value[0], errors.First().Key);

        Kind = kind;
        Title = title.Trim();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        // Türe ait olmayan parametre saklanmaz: tür değiştirilen bir fikirde eski türün
        // değerleri sessizce hesaba karışmasın.
        var p = Normalize(kind, parameters);
        InstrumentId = p.InstrumentId;
        BranchName = p.BranchName;
        CourseKind = p.CourseKind;
        Students = p.Students;
        TeacherRatePerLesson = p.TeacherRatePerLesson;
        DiscountPercent = p.DiscountPercent;
        DiscountMonths = p.DiscountMonths;
        AlreadyComingPercent = p.AlreadyComingPercent;
        PriceChangePercent = p.PriceChangePercent;
        LostStudents = p.LostStudents;
        MonthlyAmount = p.MonthlyAmount;
        OneTimeCost = p.OneTimeCost;
        UpdatedAt = now;
    }

    public static GrowthIdeaParameters Normalize(GrowthIdeaKind kind, GrowthIdeaParameters p) => kind switch
    {
        GrowthIdeaKind.NewTeacher => GrowthIdeaParameters.None with { InstrumentId = p.InstrumentId, Students = p.Students, TeacherRatePerLesson = p.TeacherRatePerLesson, OneTimeCost = p.OneTimeCost },
        GrowthIdeaKind.GroupClass => GrowthIdeaParameters.None with { InstrumentId = p.InstrumentId, Students = p.Students, TeacherRatePerLesson = p.TeacherRatePerLesson, OneTimeCost = p.OneTimeCost },
        GrowthIdeaKind.NewBranch => GrowthIdeaParameters.None with { BranchName = string.IsNullOrWhiteSpace(p.BranchName) ? null : p.BranchName.Trim(), CourseKind = p.CourseKind, Students = p.Students, TeacherRatePerLesson = p.TeacherRatePerLesson, OneTimeCost = p.OneTimeCost },
        GrowthIdeaKind.Incentive => GrowthIdeaParameters.None with { Students = p.Students, DiscountPercent = p.DiscountPercent, DiscountMonths = p.DiscountMonths, AlreadyComingPercent = p.AlreadyComingPercent, OneTimeCost = p.OneTimeCost },
        GrowthIdeaKind.PriceChange => GrowthIdeaParameters.None with { PriceChangePercent = p.PriceChangePercent, LostStudents = p.LostStudents },
        _ => GrowthIdeaParameters.None with { MonthlyAmount = p.MonthlyAmount, OneTimeCost = p.OneTimeCost },
    };

    // Alan adı → hata. Endpoint bunu doğrudan 400 gövdesine çevirir.
    public static Dictionary<string, string[]> Validate(GrowthIdeaKind kind, string? title, string? note, GrowthIdeaParameters p)
    {
        var errors = new Dictionary<string, string[]>();
        void Fail(string field, string message) => errors[field] = [message];

        if (!Enum.IsDefined(kind)) Fail("kind", "Fikir türü geçersiz.");
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > TitleMaxLength)
            Fail("title", $"Başlık zorunlu ve en fazla {TitleMaxLength} karakter.");
        if (note is { Length: > NoteMaxLength }) Fail("note", $"Not en fazla {NoteMaxLength} karakter.");

        var needsStudents = kind is GrowthIdeaKind.NewTeacher or GrowthIdeaKind.GroupClass or GrowthIdeaKind.NewBranch or GrowthIdeaKind.Incentive;
        if (needsStudents && p.Students is not (>= 1 and <= MaxStudents))
            Fail("students", $"Öğrenci sayısı 1 ile {MaxStudents} arasında olmalı.");
        if (kind is GrowthIdeaKind.NewTeacher or GrowthIdeaKind.GroupClass && p.InstrumentId is null)
            Fail("instrumentId", "Enstrüman seç.");
        if (kind == GrowthIdeaKind.GroupClass && p.Students is 1)
            Fail("students", "Grup en az 2 kişi olmalı.");
        if (kind == GrowthIdeaKind.NewBranch)
        {
            if (string.IsNullOrWhiteSpace(p.BranchName) || p.BranchName.Trim().Length > BranchNameMaxLength)
                Fail("branchName", $"Branş adı zorunlu ve en fazla {BranchNameMaxLength} karakter.");
            if (p.CourseKind is null) Fail("courseKind", "Birebir mi grup mu, seç.");
        }
        if (p.TeacherRatePerLesson is { } rate && (rate <= 0 || rate > 100_000))
            Fail("teacherRatePerLesson", "Ders başı ücret pozitif olmalı.");
        if (kind == GrowthIdeaKind.Incentive)
        {
            if (p.DiscountPercent is not (> 0 and <= 100)) Fail("discountPercent", "İndirim %1 ile %100 arasında olmalı.");
            if (p.DiscountMonths is not (>= 1 and <= 12)) Fail("discountMonths", "İndirim 1 ile 12 ay arasında sürmeli.");
            if (p.AlreadyComingPercent is not (>= 0 and <= 100)) Fail("alreadyComingPercent", "Oran %0 ile %100 arasında olmalı.");
        }
        if (kind == GrowthIdeaKind.PriceChange)
        {
            if (p.PriceChangePercent is not (>= -50 and <= 100) || p.PriceChangePercent == 0)
                Fail("priceChangePercent", "Değişim %-50 ile %100 arasında ve sıfırdan farklı olmalı.");
            if (p.LostStudents is not (>= 0 and <= MaxStudents)) Fail("lostStudents", "Kaybedilecek öğrenci 0 veya daha fazla olmalı.");
        }
        if (kind == GrowthIdeaKind.Custom && p.MonthlyAmount is null)
            Fail("monthlyAmount", "Fikrin aylık net etkisini gir.");
        if (p.MonthlyAmount is { } amount && Math.Abs(amount) > 10_000_000) Fail("monthlyAmount", "Tutar çok büyük.");
        if (p.OneTimeCost is { } cost && (cost < 0 || cost > 10_000_000)) Fail("oneTimeCost", "Tek seferlik maliyet 0 veya daha fazla olmalı.");
        return errors;
    }
}
