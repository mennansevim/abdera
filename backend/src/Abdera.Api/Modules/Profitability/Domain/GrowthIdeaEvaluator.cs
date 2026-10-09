using Abdera.Api.Modules.People.Domain;

namespace Abdera.Api.Modules.Profitability.Domain;

public enum IdeaVerdict
{
    Recommended,
    Uncertain,
    NotRecommended,
}

// MonthlyNet: fikir oturduğunda (rampa bittikten sonra) aylık ek net kâr.
// Series: önümüzdeki 12 ayın her birinde ek net kâr; tek seferlik maliyet ilk aydan düşülür.
// PaybackMonth: kümülatif toplamın sıfırı geçtiği ay (1 tabanlı); fikir baştan kârdaysa null.
public record GrowthIdeaEvaluation(
    decimal MonthlyNet,
    decimal OneTimeCost,
    decimal TwelveMonthNet,
    int? PaybackMonth,
    IReadOnlyList<decimal> Series,
    IdeaVerdict Verdict,
    IReadOnlyList<string> Reasons);

// Bir fikrin okulun bugünkü verisiyle tahmini etkisi. Saf hesap: veritabanı, saat, kültür yok.
// Rakamlar tahmindir; aidatın gerçek hesabı her zaman TuitionCalculator'dadır.
public static class GrowthIdeaEvaluator
{
    public const int Horizon = 12;
    // Doluluk eşikleri: altında yeni öğretmen değil yeni öğrenci gerekir, üstünde saat darlığı başlar.
    public const decimal LowUtilization = 0.70m;
    public const decimal HighUtilization = 0.85m;
    private const string GroupPayRule =
        "Hesap bugünkü ödeme usulüyle yapıldı: grup dersinde öğretmene her öğrenci için ayrı ders ücreti ödeniyor.";

    public static GrowthIdeaEvaluation Evaluate(GrowthIdeaKind kind, GrowthIdeaParameters p, ProfitBaseline baseline) => kind switch
    {
        GrowthIdeaKind.NewTeacher => NewTeacher(p, baseline),
        GrowthIdeaKind.GroupClass => GroupClass(p, baseline),
        GrowthIdeaKind.NewBranch => NewBranch(p, baseline),
        GrowthIdeaKind.Incentive => Incentive(p, baseline),
        GrowthIdeaKind.PriceChange => PriceChange(p, baseline),
        _ => Custom(p),
    };

    private static GrowthIdeaEvaluation NewTeacher(GrowthIdeaParameters p, ProfitBaseline b)
    {
        var reasons = new List<string>();
        var rate = TeacherRate(p, b, reasons);
        var students = p.Students ?? 0;
        var margin = b.MarginPerStudent(CourseKind.Individual, rate);
        var series = Ramp(students * margin, rampMonths: 3, p.OneTimeCost);

        var instrument = b.Instruments.FirstOrDefault(row => row.Id == p.InstrumentId);
        IdeaVerdict verdict;
        if (instrument is null || instrument.Teachers == 0)
        {
            verdict = IdeaVerdict.Uncertain;
            reasons.Insert(0, "Bu branşta henüz öğretmen yok. Önce deneme dersiyle gerçekten talep var mı gör.");
        }
        else if (instrument.Utilization is not { } utilization)
        {
            verdict = IdeaVerdict.Uncertain;
            reasons.Insert(0, $"{instrument.Name} öğretmenlerinin çalışma saatleri girilmemiş, boş saat hesaplanamıyor.");
        }
        else if (utilization < LowUtilization)
        {
            verdict = IdeaVerdict.NotRecommended;
            reasons.Insert(0, $"{instrument.Name} öğretmenlerinin haftada {Hours(instrument.SpareHours)} saati zaten boş. " +
                "Yeni öğretmene gerek yok, bu öğrenciler mevcut öğretmenlere yazılabilir. Eksik olan öğretmen değil, öğrenci talebi.");
        }
        else if (utilization >= HighUtilization)
        {
            verdict = IdeaVerdict.Recommended;
            reasons.Insert(0, $"{instrument.Name} öğretmenlerinin programı dolu ({Tl.Percent(utilization)}). Yeni öğrenci için yeni öğretmen gerekiyor.");
        }
        else
        {
            verdict = IdeaVerdict.Uncertain;
            reasons.Insert(0, $"{instrument.Name} öğretmenlerinin haftada {Hours(instrument.SpareHours)} saati hâlâ boş. Önce o saatleri doldurmak daha ucuz.");
        }

        return Build(students * margin, series, p.OneTimeCost, verdict, reasons);
    }

    private static GrowthIdeaEvaluation GroupClass(GrowthIdeaParameters p, ProfitBaseline b)
    {
        var reasons = new List<string>();
        var rate = TeacherRate(p, b, reasons);
        var size = p.Students ?? 0;
        var perStudent = b.MarginPerStudent(CourseKind.Group, rate);
        var monthly = size * perStudent;
        var groupHour = b.LessonsPerMonth > 0 ? monthly / b.LessonsPerMonth : 0;
        var individualHour = b.LessonsPerMonth > 0 ? b.MarginPerStudent(CourseKind.Individual, rate) / b.LessonsPerMonth : 0;
        var series = Ramp(monthly, rampMonths: 2, p.OneTimeCost);
        var instrument = b.Instruments.FirstOrDefault(row => row.Id == p.InstrumentId);

        IdeaVerdict verdict;
        if (perStudent <= 0)
        {
            verdict = IdeaVerdict.NotRecommended;
            reasons.Insert(0, $"Bu ücretle öğrenci başına {Tl.Format(perStudent)} kalıyor: aidat öğretmen ücretini karşılamıyor.");
        }
        else if (instrument is { SpareHours: < 1m })
        {
            verdict = IdeaVerdict.Uncertain;
            reasons.Insert(0, $"{instrument.Name} öğretmenlerinin boş saati kalmamış; grup için yeni saat açmak gerekir.");
        }
        else if (groupHour > individualHour)
        {
            verdict = IdeaVerdict.Recommended;
            var ratio = individualHour > 0 ? decimal.Round(groupHour / individualHour, 1) : 0;
            reasons.Insert(0, ratio > 0
                ? $"Bir saatlik grup dersi {Tl.Format(groupHour)}, bir saatlik birebir ders {Tl.Format(individualHour)} kazandırıyor ({ratio.ToString(System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')} kat)."
                : $"Bir saatlik grup dersi {Tl.Format(groupHour)} kazandırıyor.");
        }
        else
        {
            verdict = IdeaVerdict.Uncertain;
            reasons.Insert(0, $"Bir saatlik grup dersi ({Tl.Format(groupHour)}) birebirden ({Tl.Format(individualHour)}) az kazandırıyor; ancak yeni öğrenci getiriyorsa değer.");
        }
        reasons.Add(GroupPayRule);
        return Build(monthly, series, p.OneTimeCost, verdict, reasons);
    }

    private static GrowthIdeaEvaluation NewBranch(GrowthIdeaParameters p, ProfitBaseline b)
    {
        var reasons = new List<string>();
        var rate = TeacherRate(p, b, reasons);
        var kind = p.CourseKind ?? CourseKind.Individual;
        var students = p.Students ?? 0;
        var perStudent = b.MarginPerStudent(kind, rate);
        var monthly = students * perStudent;
        var series = Ramp(monthly, rampMonths: 3, p.OneTimeCost);

        IdeaVerdict verdict;
        if (perStudent <= 0)
        {
            verdict = IdeaVerdict.NotRecommended;
            reasons.Insert(0, $"Bu ücretle öğrenci başına {Tl.Format(perStudent)} kalıyor: aidat öğretmen ücretini karşılamıyor.");
        }
        else
        {
            verdict = IdeaVerdict.Uncertain;
            reasons.Insert(0, $"Her öğrenciden ayda {Tl.Format(perStudent)} kalır. Ama gerçekten talep var mı bilinmiyor: önce ön kayıt listesi aç, {students} kişi yazılırsa başla.");
        }
        if (kind == CourseKind.Group) reasons.Add(GroupPayRule);
        return Build(monthly, series, p.OneTimeCost, verdict, reasons);
    }

    // Teşvik indirimi: n yeni öğrenci, ilk m ay %d indirim. Öğrencilerin bir kısmı (w) indirim
    // olmadan da gelecekti - onlara verilen indirim kampanyanın gizli maliyetidir. Öğrenciler
    // ortalama kalış süresine göre ay ay azalır.
    private static GrowthIdeaEvaluation Incentive(GrowthIdeaParameters p, ProfitBaseline b)
    {
        var reasons = new List<string>();
        var rate = TeacherRate(GrowthIdeaParameters.None, b, reasons);
        var students = (decimal)(p.Students ?? 0);
        var discount = (p.DiscountPercent ?? 0) / 100m;
        var months = p.DiscountMonths ?? 0;
        var alreadyComing = (p.AlreadyComingPercent ?? 0) / 100m;
        var incremental = students * (1 - alreadyComing);
        var price = b.IndividualPrice;
        var lessonCost = b.LessonsPerMonth * rate;
        var retention = Retention(b.AverageTenureMonths);

        var series = new decimal[Horizon];
        var survival = 1m;
        decimal givenAway = 0;
        for (var month = 0; month < Horizon; month++)
        {
            var monthDiscount = month < months ? discount : 0;
            var gain = incremental * survival * (price * (1 - monthDiscount) - lessonCost);
            var lost = students * alreadyComing * survival * price * monthDiscount;
            givenAway += lost;
            series[month] = gain - lost;
            survival *= retention;
        }
        series[0] -= p.OneTimeCost ?? 0;

        var monthly = incremental * (price - lessonCost);
        var total = series.Sum();
        var verdict = total > 0 ? IdeaVerdict.Recommended : IdeaVerdict.NotRecommended;
        reasons.Insert(0, total > 0
            ? $"Bir yılda {Tl.Format(total)} kazandırıyor."
            : $"Bu haliyle kampanya bir yılda {Tl.Format(-total)} kaybettiriyor. İndirimi ya da süresini düşür.");
        if (givenAway > 0)
            reasons.Insert(1, $"Zaten gelecek öğrencilere verilen indirim: {Tl.Format(givenAway)}. Kampanyanın görünmeyen maliyeti bu.");
        return Build(monthly, series, p.OneTimeCost, verdict, reasons);
    }

    // Tarife değişikliği bir sonraki dönemden başlar: ödeme görmüş ya da açılmış aylar
    // değişmez (Receivable snapshot kuralı). Kaybedilecek öğrenci tahmini kullanıcıdan gelir.
    private static GrowthIdeaEvaluation PriceChange(GrowthIdeaParameters p, ProfitBaseline b)
    {
        var reasons = new List<string>();
        var rate = TeacherRate(GrowthIdeaParameters.None, b, reasons);
        var change = (p.PriceChangePercent ?? 0) / 100m;
        var lostStudents = p.LostStudents ?? 0;
        var added = b.MonthlyRevenue * change;
        var perStudentAfter = b.IndividualPrice * (1 + change) * (1 - b.AverageDiscountRate) - b.LessonsPerMonth * rate;
        var monthly = added - lostStudents * perStudentAfter;

        var series = new decimal[Horizon];
        for (var month = 1; month < Horizon; month++) series[month] = monthly;

        IdeaVerdict verdict;
        if (monthly <= 0)
        {
            verdict = IdeaVerdict.NotRecommended;
            reasons.Insert(0, change > 0
                ? $"{lostStudents} öğrencinin ayrılması zammın getirisini siliyor."
                : $"İndirim aylık gelirden {Tl.Format(-added)} götürüyor.");
        }
        else
        {
            verdict = IdeaVerdict.Recommended;
            var breakEven = perStudentAfter > 0 ? (int)Math.Floor(added / perStudentAfter) : 0;
            reasons.Insert(0, change > 0 && breakEven > 0
                ? $"{breakEven} öğrenciden fazlası ayrılmadıkça zam kazandırır."
                : $"Ayda {Tl.Format(monthly)} kazandırır.");
        }
        reasons.Add("Yeni fiyat gelecek aydan başlar; geçmiş aylar değişmez.");
        return Build(monthly, series, null, verdict, reasons);
    }

    private static GrowthIdeaEvaluation Custom(GrowthIdeaParameters p)
    {
        var monthly = p.MonthlyAmount ?? 0;
        var series = Enumerable.Repeat(monthly, Horizon).ToArray();
        series[0] -= p.OneTimeCost ?? 0;
        return Build(monthly, series, p.OneTimeCost, IdeaVerdict.Uncertain, ["Bu rakam senin tahminin; sistem bunu okulun verisinden kontrol edemiyor."]);
    }

    private static decimal TeacherRate(GrowthIdeaParameters p, ProfitBaseline b, List<string> reasons)
    {
        if (p.TeacherRatePerLesson is { } given) return given;
        var rate = b.RateFor(p.InstrumentId);
        if (rate <= 0)
            reasons.Add("Öğretmen ücretleri girilmemiş, öğretmen maliyeti sıfır sayıldı. Sonuç gerçekte olduğundan iyi görünüyor.");
        return rate;
    }

    // Kalış süresi T ay ise öğrencinin bir sonraki ay da kalma olasılığı 1 - 1/T.
    public static decimal Retention(decimal tenureMonths) => tenureMonths > 1 ? 1 - 1 / tenureMonths : 0;

    // Öğrenciler ilk aylarda kademeli gelir; rampa bitince tam etki. Tek seferlik maliyet ilk ayda.
    private static decimal[] Ramp(decimal monthly, int rampMonths, decimal? oneTimeCost)
    {
        var series = new decimal[Horizon];
        for (var month = 0; month < Horizon; month++)
            series[month] = monthly * Math.Min(1m, (month + 1m) / rampMonths);
        series[0] -= oneTimeCost ?? 0;
        return series;
    }

    private static GrowthIdeaEvaluation Build(decimal monthly, decimal[] series, decimal? oneTimeCost, IdeaVerdict verdict, List<string> reasons)
    {
        var rounded = series.Select(value => decimal.Round(value, 0, MidpointRounding.AwayFromZero)).ToList();
        return new GrowthIdeaEvaluation(
            decimal.Round(monthly, 0, MidpointRounding.AwayFromZero),
            oneTimeCost ?? 0,
            rounded.Sum(),
            Payback(rounded),
            rounded,
            verdict,
            reasons.Distinct().ToList());
    }

    private static int? Payback(IReadOnlyList<decimal> series)
    {
        if (series.Count == 0 || series[0] >= 0) return null;
        decimal cumulative = 0;
        for (var month = 0; month < series.Count; month++)
        {
            cumulative += series[month];
            if (cumulative >= 0) return month + 1;
        }
        return null;
    }

    private static string Hours(decimal? hours) =>
        decimal.Round(hours ?? 0, 0, MidpointRounding.AwayFromZero).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
