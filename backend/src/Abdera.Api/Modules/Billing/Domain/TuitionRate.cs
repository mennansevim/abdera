using Abdera.Api.Modules.People.Domain;

namespace Abdera.Api.Modules.Billing.Domain;

// Okulun ücret tarifesi. Eski price_lists + price_list_items ikilisinin yerini alır.
// Kasıtlı olarak TEK eksene indirildi: ders birebir mi grup mu (bkz. CourseKind).
// Enstrüman ve ders süresi fiyatı değiştirmiyor - eski model bu iki ekseni taşıdığı için
// bir aidat açabilmek dört kavram gerektiriyordu (docs/10-decisions.md H1).
//
// Zam = yeni EffectiveFrom'lu yeni satır. Geçmiş aidatlar tutarını kendi satırına
// kopyaladığı için (docs/10-decisions.md A1 fiyat snapshot'ı) etkilenmez.
public class TuitionRate
{
    public Guid Id { get; private set; }
    public CourseKind CourseKind { get; private set; }
    // Bilgi amaçlı - veliye gönderilen metinde "4 ders" diye geçiyor. Tutar aylık sabit,
    // ders başına bölünmüyor (okul kuralı: "4 hafta içinde 4 dersimizi tamamlamak durumundayız").
    public int LessonsPerMonth { get; private set; }
    public decimal MonthlyAmount { get; private set; }
    public string Currency { get; private set; } = "TRY";
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveUntil { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }

    private TuitionRate() { }

    public static TuitionRate Create(
        CourseKind courseKind, int lessonsPerMonth, decimal monthlyAmount, string currency,
        DateOnly effectiveFrom, Guid? createdBy, DateTimeOffset now)
    {
        if (monthlyAmount < 0) throw new ArgumentException("Tutar negatif olamaz.", nameof(monthlyAmount));
        if (lessonsPerMonth is < 1 or > 31)
            throw new ArgumentException("Aylık ders sayısı 1 ile 31 arasında olmalı.", nameof(lessonsPerMonth));

        return new TuitionRate
        {
            Id = Guid.NewGuid(),
            CourseKind = courseKind,
            LessonsPerMonth = lessonsPerMonth,
            MonthlyAmount = monthlyAmount,
            Currency = string.IsNullOrWhiteSpace(currency) ? "TRY" : currency.Trim().ToUpperInvariant(),
            EffectiveFrom = effectiveFrom,
            CreatedAt = now,
            CreatedBy = createdBy,
        };
    }

    public bool IsActiveOn(DateOnly date) =>
        EffectiveFrom <= date && (EffectiveUntil is null || date <= EffectiveUntil);

    // Yeni tarife yürürlüğe girerken bir öncekini kapatmak için. Bitiş, yeni tarifenin
    // başlangıcından bir gün öncesidir - böylece hiçbir gün iki tarifeye birden düşmez.
    public void EndOn(DateOnly effectiveUntil)
    {
        if (effectiveUntil < EffectiveFrom)
            throw new ArgumentException("Bitiş tarihi başlangıçtan önce olamaz.", nameof(effectiveUntil));

        EffectiveUntil = effectiveUntil;
    }
}
