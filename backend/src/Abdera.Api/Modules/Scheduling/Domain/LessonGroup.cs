using Abdera.Api.Shared;

namespace Abdera.Api.Modules.Scheduling.Domain;

public enum LessonGroupStatus
{
    Active,
    Ended,
}

// Grup dersi sınıfı (Resim gibi): bir öğretmen, bir kurs, haftada bir sabit gün/saat ve en
// fazla Capacity öğrenci. D6'nın "Lesson.studentId tekil" sınırı korunur: ders satırları yine
// öğrenci başına üretilir (yoklama, telafi, aidat öğrenci bazlı kalır), her üyenin kendi
// LessonSeries'i bu gruba LessonGroupId ile bağlanır. Grup yalnızca "bu seriler aynı sınıf"
// bilgisini ve kapasite/saat kuralını taşır - takvim bu bağdan dersleri tek kart olarak çizer,
// çakışma kontrolü aynı gruptaki serileri öğretmen çakışması saymaz.
public class LessonGroup
{
    public const int DefaultDurationMinutes = 90;
    public const int DefaultCapacity = 6;
    public const int MaximumCapacity = 12;
    public const int NameMaxLength = 80;

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public Guid TeacherId { get; private set; }
    public Guid InstrumentId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly StartTime { get; private set; }
    public int DurationMinutes { get; private set; }
    public int Capacity { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public LessonGroupStatus Status { get; private set; } = LessonGroupStatus.Active;
    public DateOnly? EndedOn { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private LessonGroup() { }

    public static LessonGroup Create(
        string name, Guid teacherId, Guid instrumentId, DayOfWeek dayOfWeek, TimeOnly startTime,
        int durationMinutes, int capacity, DateOnly effectiveFrom, DateTimeOffset now)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("Grup adı boş olamaz.", nameof(name));
        if (trimmed.Length > NameMaxLength) throw new ArgumentException($"Grup adı en fazla {NameMaxLength} karakter olabilir.", nameof(name));
        if (durationMinutes is < Lesson.MinimumDurationMinutes or > Lesson.MaximumDurationMinutes)
            throw new ArgumentOutOfRangeException(nameof(durationMinutes), $"Ders süresi {Lesson.MinimumDurationMinutes}–{Lesson.MaximumDurationMinutes} dakika arasında olmalı.");
        if (capacity is < 2 or > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity), $"Grup kapasitesi 2 ile {MaximumCapacity} arasında olmalı.");
        if (effectiveFrom == default) throw new ArgumentException("Başlangıç tarihi zorunlu.", nameof(effectiveFrom));

        return new LessonGroup
        {
            Id = Guid.NewGuid(),
            Name = trimmed,
            TeacherId = teacherId,
            InstrumentId = instrumentId,
            DayOfWeek = dayOfWeek,
            StartTime = startTime,
            DurationMinutes = durationMinutes,
            Capacity = capacity,
            EffectiveFrom = effectiveFrom,
            Status = LessonGroupStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public TimeOnly EndTime => StartTime.AddMinutes(DurationMinutes);

    // Kapasite kontrolü çağıranda yapılır (aktif üye sayısı başka tablodan gelir); bu çağrı
    // satırı değiştirerek xmin'i ilerletir - aynı anda iki ekleme son boş yeri birlikte
    // dolduramaz, ikincisi DbUpdateConcurrencyException ile düşer.
    public void EnsureCanAdmit(int activeMembers, DateTimeOffset now)
    {
        if (Status != LessonGroupStatus.Active)
            throw new ConflictException("Kapatılmış bir gruba öğrenci eklenemez.");
        if (activeMembers >= Capacity)
            throw new ConflictException($"Grup dolu ({Capacity}/{Capacity} öğrenci).");
        UpdatedAt = now;
    }

    public void Rename(string name, DateTimeOffset now)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new ArgumentException("Grup adı boş olamaz.", nameof(name));
        if (trimmed.Length > NameMaxLength) throw new ArgumentException($"Grup adı en fazla {NameMaxLength} karakter olabilir.", nameof(name));
        Name = trimmed;
        UpdatedAt = now;
    }

    public void SetCapacity(int capacity, int activeMembers, DateTimeOffset now)
    {
        if (capacity is < 2 or > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity), $"Grup kapasitesi 2 ile {MaximumCapacity} arasında olmalı.");
        if (capacity < activeMembers)
            throw new ConflictException($"Grupta {activeMembers} öğrenci var; kapasite bunun altına indirilemez.");
        Capacity = capacity;
        UpdatedAt = now;
    }

    // Üyesi kalmayan grup kapatılır; üyeli grubu kapatmak öğrencilerin programını sessizce
    // yetim bırakırdı (çağıran üye sayısını kontrol eder).
    public void End(DateOnly endedOn, DateTimeOffset now)
    {
        Status = LessonGroupStatus.Ended;
        EndedOn = endedOn;
        UpdatedAt = now;
    }
}
