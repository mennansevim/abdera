namespace Abdera.Api.Modules.Scheduling.Domain;

// Yeni kayıt ekranındaki "kim, hangi saatte boş" sorusunun hesabı. Saf fonksiyon - veritabanına
// bağımlı değil, birim testiyle doğrulanır (docs/09-testing.md).
//
// "Boş" HAFTALIK bir kavramdır: yeni öğrenci her hafta aynı saate yazılacağı için ölçüt
// öğretmenin uygunluk penceresi eksi aktif ders serileridir. Tek seferlik bir ders (telafi,
// taşınmış ders), izin ve tatil saati kalıcı olarak doldurmaz; yalnızca o saatin ilk
// kullanılabilir tarihini (FirstOpenDate) ileri iter.
public static class OpenSlotFinder
{
    public const int StepMinutes = 15;

    public record Window(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeOnly EndTime);
    public record Busy(DayOfWeek DayOfWeek, TimeOnly StartTime, int DurationMinutes);
    public record Slot(DayOfWeek DayOfWeek, TimeOnly StartTime);

    // TimeOnly.AddMinutes gece yarısında başa sarar; karşılaştırmalar dakika cinsinden
    // yapılır ki 23:30'da başlayan 45 dakikalık bir aralık 00:15'te bitmiş sayılmasın.
    private static int Minutes(TimeOnly time) => time.Hour * 60 + time.Minute;

    public static IReadOnlyList<Slot> WeeklyFreeStarts(
        IEnumerable<Window> windows, IEnumerable<Busy> busy, int durationMinutes)
    {
        if (durationMinutes <= 0)
            throw new ArgumentException("Ders süresi pozitif olmalı.", nameof(durationMinutes));

        var busyByDay = busy.ToLookup(b => b.DayOfWeek);
        var starts = new SortedSet<(DayOfWeek Day, int Start)>();

        foreach (var window in windows)
        {
            var windowStart = Minutes(window.StartTime);
            var windowEnd = Minutes(window.EndTime);

            for (var start = windowStart; start + durationMinutes <= windowEnd; start += StepMinutes)
            {
                var end = start + durationMinutes;
                var overlaps = busyByDay[window.DayOfWeek].Any(b =>
                    start < Minutes(b.StartTime) + b.DurationMinutes && Minutes(b.StartTime) < end);
                if (!overlaps) starts.Add((window.DayOfWeek, start));
            }
        }

        // Pazartesi başta, Pazar sonda: okulun haftası Pzt-Cmt (TeacherPayWeek ile aynı sıra).
        return starts
            .OrderBy(s => ((int)s.Day + 6) % 7).ThenBy(s => s.Start)
            .Select(s => new Slot(s.Day, new TimeOnly(s.Start / 60, s.Start % 60)))
            .ToList();
    }

    // from dahil, en fazla `weeks` hafta ileriye bakar; o gün tatil/izin değilse ve o saatte
    // tek seferlik bir ders yoksa ilk tarih odur. Bulunamazsa null - pencere boyunca her hafta
    // bir engel var demektir. notBeforeOnFrom: `from` bugünse şu anki saat - saati geçmiş bir
    // ders bugün "en erken" sayılmasın.
    public static DateOnly? FirstOpenDate(
        Slot slot, int durationMinutes, DateOnly from, int weeks,
        Func<DateOnly, bool> isDayBlocked,
        IReadOnlyCollection<(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime)> oneOffLessons,
        TimeOnly? notBeforeOnFrom = null)
    {
        var start = Minutes(slot.StartTime);
        var end = start + durationMinutes;
        var first = from.AddDays(((int)slot.DayOfWeek - (int)from.DayOfWeek + 7) % 7);

        for (var week = 0; week < weeks; week++)
        {
            var date = first.AddDays(7 * week);
            if (isDayBlocked(date)) continue;
            if (date == from && notBeforeOnFrom is { } notBefore && slot.StartTime < notBefore) continue;

            var taken = oneOffLessons.Any(l =>
                l.Date == date && start < Minutes(l.EndTime) && Minutes(l.StartTime) < end);
            if (!taken) return date;
        }

        return null;
    }
}
