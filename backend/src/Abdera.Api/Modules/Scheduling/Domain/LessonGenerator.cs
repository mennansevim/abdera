namespace Abdera.Api.Modules.Scheduling.Domain;

// docs/00-master-prompt.md: "Generate concrete occurrences for a rolling window... Generation
// must be idempotent and must not create duplicates when run twice." docs/10-decisions.md A3:
// tatil ve öğretmen izni günleri atlanır. Saf fonksiyon - veritabanına bağımlı değil, bu yüzden
// docs/09-testing.md'nin istediği gibi saf birim testiyle doğrulanabilir.
public static class LessonGenerator
{
    public record Occurrence(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime);

    public record GenerationPlan(
        IReadOnlyList<Occurrence> ToCreate,
        IReadOnlyList<DateOnly> SkippedHolidays,
        IReadOnlyList<DateOnly> SkippedTeacherTimeOff,
        IReadOnlyList<DateOnly> AlreadyExists);

    /// <summary>
    /// windowStart/windowEnd dahil aralıkta, series'in gün/saatine denk gelen occurrence'ları
    /// hesaplar. Tatil günleri ve öğretmen izinleri atlanır; zaten üretilmiş (existingDates)
    /// tarihler yeniden oluşturulmaz - bu üç kontrol idempotency ve A3'ü birlikte sağlar.
    /// </summary>
    public static GenerationPlan Plan(
        LessonSeries series,
        DateOnly windowStart,
        DateOnly windowEnd,
        IReadOnlySet<DateOnly> existingDates,
        IReadOnlySet<DateOnly> holidayDates,
        IReadOnlyCollection<(DateOnly StartsOn, DateOnly EndsOn)> teacherTimeOffRanges)
    {
        var effectiveStart = series.EffectiveFrom > windowStart ? series.EffectiveFrom : windowStart;
        var effectiveEnd = series.EffectiveUntil is { } until && until < windowEnd ? until : windowEnd;

        var toCreate = new List<Occurrence>();
        var skippedHolidays = new List<DateOnly>();
        var skippedTimeOff = new List<DateOnly>();
        var alreadyExists = new List<DateOnly>();

        if (effectiveStart > effectiveEnd)
        {
            return new GenerationPlan(toCreate, skippedHolidays, skippedTimeOff, alreadyExists);
        }

        var firstOccurrence = NextOrSame(effectiveStart, series.DayOfWeek);

        for (var date = firstOccurrence; date <= effectiveEnd; date = date.AddDays(7))
        {
            if (existingDates.Contains(date))
            {
                alreadyExists.Add(date);
                continue;
            }

            if (holidayDates.Contains(date))
            {
                skippedHolidays.Add(date);
                continue;
            }

            if (teacherTimeOffRanges.Any(r => date >= r.StartsOn && date <= r.EndsOn))
            {
                skippedTimeOff.Add(date);
                continue;
            }

            toCreate.Add(new Occurrence(date, series.StartTime, series.EndTime));
        }

        return new GenerationPlan(toCreate, skippedHolidays, skippedTimeOff, alreadyExists);
    }

    private static DateOnly NextOrSame(DateOnly from, DayOfWeek targetDay)
    {
        var diff = ((int)targetDay - (int)from.DayOfWeek + 7) % 7;
        return from.AddDays(diff);
    }

    // Türkiye 2016'dan beri sabit UTC+3, DST yok (docs/10-decisions.md D5) - yine de doğru
    // yöntem TimeZoneInfo üzerinden geçmek, "+3" hardcode etmek değil.
    public static DateTimeOffset ToUtcInstant(DateOnly date, TimeOnly time, TimeZoneInfo schoolTimeZone)
    {
        var localDateTime = date.ToDateTime(time, DateTimeKind.Unspecified);
        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(localDateTime, schoolTimeZone);
        return new DateTimeOffset(utcDateTime, TimeSpan.Zero);
    }
}
