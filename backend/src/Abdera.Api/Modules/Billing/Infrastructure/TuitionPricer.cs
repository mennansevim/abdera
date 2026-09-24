using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Infrastructure;

// Bir aidatın tutarını belirleyen HER ŞEYİ tek seferde yükler: tarife, indirim politikası,
// kademeler ve "bu öğrenci 2 kursa mı gidiyor / kardeşi var mı" olguları.
//
// Tek sınıfta toplanmasının sebebi: aylık üretim (MonthlyDueRun) ile peşin ödeme
// (PrepayPlans) aynı hesabı yapmak zorunda. Eskiden bu iş BulkReceivables ve BulkPayments
// içinde iki ayrı kopya hâlindeydi ve biri diğerinden farklı davranıyordu (toplu ödeme
// tutarın indirimsiz toplama birebir eşit olmasını şart koşuyordu - kampanyanın sisteme
// girilememesinin sebebi tam olarak buydu).
public sealed class TuitionPricer
{
    private readonly List<TuitionRate> _rates;
    private readonly HashSet<Guid> _multiCourseStudents;
    private readonly HashSet<Guid> _studentsWithSibling;

    private TuitionPricer(
        BillingSettings settings,
        List<TuitionRate> rates,
        List<PrepayDiscountTier> tiers,
        HashSet<Guid> multiCourseStudents,
        HashSet<Guid> studentsWithSibling)
    {
        Settings = settings;
        _rates = rates;
        PrepayTiers = tiers;
        _multiCourseStudents = multiCourseStudents;
        _studentsWithSibling = studentsWithSibling;
    }

    public BillingSettings Settings { get; }
    public IReadOnlyList<PrepayDiscountTier> PrepayTiers { get; }

    public static async Task<TuitionPricer> LoadAsync(AbderaDbContext db)
    {
        var settings = await BillingSettings.GetCurrentAsync(db);
        var rates = await db.TuitionRates.AsNoTracking().ToListAsync();
        var tiers = await db.PrepayDiscountTiers.AsNoTracking().OrderBy(tier => tier.MinMonths).ToListAsync();

        // İndirimler yalnızca AKTİF kayıtlara bakar. Biten bir kurs "2. kurs" saymaz,
        // okuldan ayrılmış bir kardeş kardeş indirimi doğurmaz.
        var activeEnrollments = await db.Enrollments
            .Where(enrollment => enrollment.Status == EnrollmentStatus.Active)
            .Select(enrollment => new { enrollment.StudentId })
            .ToListAsync();

        var multiCourse = activeEnrollments
            .GroupBy(enrollment => enrollment.StudentId)
            .Where(group => group.Count() >= 2)
            .Select(group => group.Key)
            .ToHashSet();

        // Kardeş indirimi ARTIK ÇIKARIM DEĞİL (docs/10-decisions.md H13). Eskiden burada
        // student_guardians üzerinden "aynı veliye bağlı 2+ aktif öğrenci" sorgusu vardı;
        // aynı veli iki kez kaydedildiğinde gerçek kardeşlere indirim vermiyor, bir veli
        // akraba/komşu çocuğuna da bağlandığında kardeş olmayana veriyordu. Yönetici artık
        // öğrenci künyesindeki kutuyu işaretler ve karar tek bir yerde görünür olur.
        //
        // Çoklu kurs indirimi (aşağıdaki multiCourse) çıkarım olarak KALDI: aktif kurs
        // kaydı sayısı sistemin kendi verisi, tahmin değil.
        var siblings = await db.Students
            .Where(student => student.SiblingDiscount)
            .Select(student => student.Id)
            .ToListAsync();

        return new TuitionPricer(settings, rates, tiers, multiCourse, siblings.ToHashSet());
    }

    // Verilen günde yürürlükte olan tarife. Yoksa null - çağıran bunu "eksik" olarak
    // kullanıcıya gösterir, sessizce 0 TL'lik aidat üretmez.
    public TuitionRate? RateFor(CourseKind courseKind, DateOnly on) =>
        _rates
            .Where(rate => rate.CourseKind == courseKind && rate.IsActiveOn(on))
            .OrderByDescending(rate => rate.EffectiveFrom)
            .FirstOrDefault();

    // "Tarife yok" hatasını açıklamak için: bu ders türünün en erken tarifesi nerede başlıyor.
    // Dönem ondan önceyse sebep net - tarifeler bu aydan sonra başlıyor.
    public DateOnly? EarliestRateStart(CourseKind courseKind) =>
        _rates.Where(rate => rate.CourseKind == courseKind)
            .Select(rate => (DateOnly?)rate.EffectiveFrom)
            .Min();

    // Kullanıcıya gösterilen "tarife yok" metni - aidat açma, peşin ödeme ve aylık üretim aynı
    // cümleyi kursun. Tohum tarifesi 1 Eylül 2026'da başladığı için en sık rastlanan durum
    // sezondan önceki bir ayı seçmek; o durumda ne yapılacağı da söylenir.
    public string MissingRateMessage(CourseKind courseKind, string period)
    {
        var kindLabel = courseKind == CourseKind.Group ? "Grup" : "Birebir";
        var earliest = EarliestRateStart(courseKind);
        if (earliest is null)
            return $"{period}: {kindLabel} dersi için hiç ücret tarifesi tanımlı değil. Fiyat politikası ekranından tarife tanımlayın.";
        if (BillingPeriod.FirstDay(period) < earliest)
            return $"{period}: {kindLabel} ders tarifesi {earliest:yyyy-MM-dd} tarihinde başlıyor, bu ayı kapsamıyor. " +
                   $"İlk dönemi {earliest:yyyy-MM} yapın ya da Fiyat politikası ekranından {period}-01 başlangıçlı bir tarife ekleyin.";
        return $"{period}: {kindLabel} dersi için bu ayı kapsayan ücret tarifesi yok. Fiyat politikası ekranından tarife tanımlayın.";
    }

    public TuitionCalculator.DiscountContext ContextFor(Enrollment enrollment) => new(
        AttendsMultipleCourses: _multiCourseStudents.Contains(enrollment.StudentId),
        HasSibling: _studentsWithSibling.Contains(enrollment.StudentId),
        ManualPercent: enrollment.ManualDiscountPercent,
        ManualReason: enrollment.ManualDiscountReason,
        MultiCoursePercent: Settings.MultiCourseDiscountPercent,
        SiblingPercent: Settings.SiblingDiscountPercent);

    public decimal PrepayPercentFor(int months) => TuitionCalculator.ResolvePrepayPercent(PrepayTiers, months);

    // Tek ayın aidatı. prepayPercent 0 ise yalnızca öğrenci indirimi uygulanır.
    public (TuitionRate Rate, TuitionCalculator.Breakdown Breakdown)? Price(
        Enrollment enrollment, string period, decimal prepayPercent = 0m)
    {
        var rate = RateFor(enrollment.CourseKind, BillingPeriod.FirstDay(period));
        if (rate is null) return null;

        var context = ContextFor(enrollment);
        var breakdown = prepayPercent > 0
            ? TuitionCalculator.ComputePrepaidMonthly(rate.MonthlyAmount, context, prepayPercent)
            : TuitionCalculator.ComputeMonthly(rate.MonthlyAmount, context);

        return (rate, breakdown);
    }

    public DateOnly DueDateFor(string period) => BillingPeriod.DueDate(period, Settings.DueDayOfMonth);
}
