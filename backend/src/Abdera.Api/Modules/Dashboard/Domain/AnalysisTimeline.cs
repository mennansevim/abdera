using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Scheduling.Domain;

namespace Abdera.Api.Modules.Dashboard.Domain;

// Öğrenci/öğretmen analizi ekranlarındaki "aylık şerit" in tek hesap yeri: hangi aylar
// gösterilir, bir ders hangi kovaya düşer, devam oranı nasıl hesaplanır. İki ekran da aynı
// kuralları kullanır ki öğrencinin %90'ı ile öğretmenin listesindeki %90 aynı şeyi söylesin.
public enum AnalysisLessonState
{
    Present,
    Makeup,
    Excused,
    Absent,
    Cancelled,
    // Ders geçti ama yoklama girilmedi - devam oranına girmez, ayrıca gösterilir.
    Unmarked,
    // Ayın henüz gelmemiş dersi.
    Upcoming,
}

public static class AnalysisTimeline
{
    public const int DefaultMonths = 6;
    public const int MinMonths = 3;
    public const int MaxMonths = 12;

    public static int NormalizeMonths(int? months) =>
        Math.Clamp(months ?? DefaultMonths, MinMonths, MaxMonths);

    // Eskiden yeniye "yyyy-MM" etiketleri; son eleman okulun yerel takvimindeki bu ay.
    public static IReadOnlyList<string> Periods(DateOnly todayLocal, int months)
    {
        var first = new DateOnly(todayLocal.Year, todayLocal.Month, 1).AddMonths(-(months - 1));
        return Enumerable.Range(0, months)
            .Select(offset => first.AddMonths(offset))
            .Select(month => PeriodOf(month))
            .ToList();
    }

    public static string PeriodOf(DateOnly date) => $"{date.Year:D4}-{date.Month:D2}";

    public static DateOnly FirstDay(string period) =>
        new(int.Parse(period[..4]), int.Parse(period[5..]), 1);

    // RESCHEDULED satırlar yeni saatiyle ayrı bir satır olarak zaten listede (docs/05-state-models.md),
    // çağıran onları hiç getirmez. Telafi dersi (MAKEUP) geldiyse ayrı bir kova: veliye/yöneticiye
    // "kaçırdığını telafi etti" bilgisi düz "geldi"den daha anlamlı.
    public static AnalysisLessonState Classify(
        LessonStatus status, AttendanceStatus? attendance, DateTimeOffset startAt, DateTimeOffset now)
    {
        if (status == LessonStatus.Cancelled) return AnalysisLessonState.Cancelled;
        return attendance switch
        {
            AttendanceStatus.Present => status == LessonStatus.Makeup ? AnalysisLessonState.Makeup : AnalysisLessonState.Present,
            AttendanceStatus.Excused => AnalysisLessonState.Excused,
            AttendanceStatus.Absent => AnalysisLessonState.Absent,
            _ => startAt <= now ? AnalysisLessonState.Unmarked : AnalysisLessonState.Upcoming,
        };
    }

    public static bool IsAttended(AnalysisLessonState state) =>
        state is AnalysisLessonState.Present or AnalysisLessonState.Makeup;

    // Devam oranının paydası yoklaması girilmiş ve yapılmış dersler: iptal, yoklamasız ve
    // gelecek dersler orana girmez. Hiç ders yoksa oran yok (0 değil) - "0%" yanlış alarm verir.
    public static bool CountsTowardRate(AnalysisLessonState state) =>
        state is AnalysisLessonState.Present or AnalysisLessonState.Makeup
            or AnalysisLessonState.Excused or AnalysisLessonState.Absent;

    public static int? Rate(int part, int whole) =>
        whole == 0 ? null : (int)Math.Round(part * 100m / whole, MidpointRounding.AwayFromZero);

    // Aynı ayın birden fazla aidat satırı (iki kurs) tek hücrede gösterilir: gecikme varsa o
    // öne çıkar, hepsi ödendiyse ödendi, bir kısmına para girdiyse kısmi.
    public static ReceivableStatus CombineBillingStatus(IEnumerable<ReceivableStatus> statuses)
    {
        var list = statuses.Where(s => s != ReceivableStatus.Cancelled).ToList();
        if (list.Count == 0) return ReceivableStatus.Cancelled;
        if (list.Contains(ReceivableStatus.Overdue)) return ReceivableStatus.Overdue;
        if (list.All(s => s == ReceivableStatus.Paid)) return ReceivableStatus.Paid;
        if (list.Any(s => s is ReceivableStatus.Paid or ReceivableStatus.Partial)) return ReceivableStatus.Partial;
        return ReceivableStatus.Unpaid;
    }

    public static decimal? Average(IEnumerable<int> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Math.Round((decimal)list.Average(), 1, MidpointRounding.AwayFromZero);
    }
}
