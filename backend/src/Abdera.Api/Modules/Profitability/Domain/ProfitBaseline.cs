using System.Globalization;
using Abdera.Api.Modules.People.Domain;

namespace Abdera.Api.Modules.Profitability.Domain;

// Kârlılık hesabının girdisi: okulun bugünkü birim ekonomisi. ProfitabilitySnapshot bunu
// veritabanından bir kez kurar; fikir değerlendirici, içgörüler ve AI yorumu aynı nesneyi okur
// - üç ayrı yerde üç ayrı "öğrenci başı kâr" hesabı olmasın.
//
// Öğretmen maliyeti bugünkü ödeme kuralıyla hesaplanır (TeacherPayouts): öğretmen tamamlanan
// her DERS SATIRI için ücret alır ve ders satırları öğrenci başına üretilir. Grup dersinde de
// öğretmen her öğrenci için bir ders ücreti alır; bu yüzden grubun öğrenci başı maliyeti
// birebirle aynıdır, farkı yalnızca daha düşük aidattır.
public record ProfitBaseline(
    decimal IndividualPrice,
    decimal GroupPrice,
    int LessonsPerMonth,
    // 0..1 - bu ayın aidatlarında tarifeden düşülen ortalama indirim oranı.
    decimal AverageDiscountRate,
    // Ücreti girilmiş öğretmenlerin ortalaması; hiç ücret yoksa null.
    decimal? AverageTeacherRate,
    decimal MonthlyRevenue,
    int ActiveEnrollments,
    decimal AverageTenureMonths,
    bool TenureIsDefault,
    IReadOnlyList<InstrumentStats> Instruments)
{
    public const decimal DefaultTenureMonths = 9m;
    public const int MinimumEndedForTenure = 5;

    public decimal PriceOf(CourseKind kind) => kind == CourseKind.Group ? GroupPrice : IndividualPrice;

    // Öğrenci başına aylık katkı: indirim sonrası aidat - ayın ders ücretleri.
    public decimal MarginPerStudent(CourseKind kind, decimal teacherRate) =>
        PriceOf(kind) * (1 - AverageDiscountRate) - LessonsPerMonth * teacherRate;

    public decimal RateFor(Guid? instrumentId) =>
        Instruments.FirstOrDefault(row => row.Id == instrumentId)?.TeacherRate ?? AverageTeacherRate ?? 0;

    public decimal LifetimeValue(decimal teacherRate) => MarginPerStudent(CourseKind.Individual, teacherRate) * AverageTenureMonths;
}

public record InstrumentStats(
    Guid Id,
    string Name,
    int Individual,
    int Group,
    int Teachers,
    // Bu enstrümanı veren öğretmenlerin ortalama ders başı ücreti (girilmişse).
    decimal? TeacherRate,
    // Haftalık dolu ders saati (aynı saatteki grup dersi bir kez sayılır).
    decimal BookedHours,
    // Öğretmenlerin haftalık müsaitlik saati toplamı; müsaitlik girilmemişse null.
    decimal? AvailableHours)
{
    public decimal? Utilization => AvailableHours is > 0 ? Math.Min(1.5m, BookedHours / AvailableHours.Value) : null;
    public decimal? SpareHours => AvailableHours is { } available ? Math.Max(0, available - BookedHours) : null;
}

// Kullanıcıya görünen tutar metni. Adlandırılmış kültür (tr-TR) yerine elle binlik ayırıcı:
// birim testleri ve Alpine imajı kültür kurulumuna bağlı kalmasın (CLAUDE.md ICU notu).
public static class Tl
{
    public static string Format(decimal value)
    {
        var rounded = decimal.Round(Math.Abs(value), 0, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.');
        return value < 0 && rounded > 0 ? $"−₺{text}" : $"₺{text}";
    }

    public static string Percent(decimal ratio) =>
        "%" + decimal.Round(ratio * 100, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
}
