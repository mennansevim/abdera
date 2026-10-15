using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Instance.Domain;

// Okula özel kurallar (docs/multi-tenant/ARCHITECTURE.md §5.3). Bugüne kadar koda ya da ortam
// değişkenine yazılıydı; her okul kendi değerini seçebilsin diye tek satırlık bu tabloya taşındı.
// "Hesabın tek yeri" kuralı değişmez: GroupLessonPay, CancelLesson vb. yine hesabı yapar, yalnızca
// sabiti buradan okur.
//
// Satır yoksa bugünkü davranış birebir korunur: sabitler koddaki varsayılanlar, telafi süreleri
// ise eskiden olduğu gibi Policy:* yapılandırmasından gelir.
public class SchoolPolicy
{
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-0000000000c2");

    public static readonly TimeOnly DefaultSchoolDayStart = new(9, 0);
    public static readonly TimeOnly DefaultSchoolDayEnd = new(21, 0);

    public Guid Id { get; private set; }
    public decimal GroupLessonSharePercent { get; private set; } = GroupLessonPay.SharePercent;
    public int MaxWeeklyLessons { get; private set; } = StudentWeeklyLessonPolicy.MaximumLessons;
    public int MakeupNoticeHours { get; private set; } = 24;
    public int MakeupCreditValidDays { get; private set; } = 60;
    public int ExcusedMakeupWindowDays { get; private set; } = 21;
    public TimeOnly SchoolDayStart { get; private set; } = DefaultSchoolDayStart;
    public TimeOnly SchoolDayEnd { get; private set; } = DefaultSchoolDayEnd;
    public Guid? UpdatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public uint RowVersion { get; private set; }

    private SchoolPolicy() { }

    // config verilmezse (yalnızca sabit kurallara bakan çağrılar) telafi süreleri koddaki varsayılandır.
    public static SchoolPolicy CreateDefault(IConfiguration? config, DateTimeOffset now) => new()
    {
        Id = SingletonId,
        MakeupNoticeHours = config?.GetValue("Policy:MakeupNoticeHours", 24) ?? 24,
        MakeupCreditValidDays = config?.GetValue("Policy:MakeupCreditValidDays", 60) ?? 60,
        ExcusedMakeupWindowDays = config?.GetValue("Policy:ExcusedMakeupWindowDays", 21) ?? 21,
        CreatedAt = now,
        UpdatedAt = now,
    };

    // Satır yoksa kalıcı olmayan bir varsayılan döner - salt okuma çağrısı Add/Save tetiklemez.
    public static async Task<SchoolPolicy> GetCurrentAsync(AbderaDbContext db, IConfiguration? config = null) =>
        await db.SchoolPolicies.AsNoTracking().SingleOrDefaultAsync(p => p.Id == SingletonId)
        ?? CreateDefault(config, DateTimeOffset.MinValue);

    public void Update(
        decimal groupLessonSharePercent, int maxWeeklyLessons, int makeupNoticeHours, int makeupCreditValidDays,
        int excusedMakeupWindowDays, TimeOnly schoolDayStart, TimeOnly schoolDayEnd, Guid? updatedBy, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (groupLessonSharePercent is < 0 or > 100 || decimal.Round(groupLessonSharePercent, 2) != groupLessonSharePercent)
            errors["groupLessonSharePercent"] = ["Grup dersi payı 0 ile 100 arasında, en fazla iki ondalıklı olmalı."];
        if (maxWeeklyLessons is < 1 or > 14)
            errors["maxWeeklyLessons"] = ["Haftalık ders sınırı 1 ile 14 arasında olmalı."];
        if (makeupNoticeHours is < 0 or > 168)
            errors["makeupNoticeHours"] = ["Telafi bildirim süresi 0 ile 168 saat arasında olmalı."];
        if (makeupCreditValidDays is < 1 or > 365)
            errors["makeupCreditValidDays"] = ["Telafi hakkının geçerliliği 1 ile 365 gün arasında olmalı."];
        if (excusedMakeupWindowDays is < 1 or > 365)
            errors["excusedMakeupWindowDays"] = ["Mazeretli telafi süresi 1 ile 365 gün arasında olmalı."];
        if (schoolDayEnd <= schoolDayStart)
            errors["schoolDayEnd"] = ["Okul günü bitişi başlangıçtan sonra olmalı."];
        if (schoolDayStart.Minute % 15 != 0 || schoolDayEnd.Minute % 15 != 0)
            errors["schoolDayStart"] = ["Okul günü saatleri çeyrek saatlik olmalı (örn. 09:00, 09:15)."];
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        GroupLessonSharePercent = groupLessonSharePercent;
        MaxWeeklyLessons = maxWeeklyLessons;
        MakeupNoticeHours = makeupNoticeHours;
        MakeupCreditValidDays = makeupCreditValidDays;
        ExcusedMakeupWindowDays = excusedMakeupWindowDays;
        SchoolDayStart = schoolDayStart;
        SchoolDayEnd = schoolDayEnd;
        UpdatedBy = updatedBy;
        UpdatedAt = now;
    }
}
