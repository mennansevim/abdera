using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Profitability.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Profitability.Features;

// Kârlılık ekranının veri katmanı. Dashboard gibi salt-okunur bir toplulaştırıcıdır
// (docs/02-modules.md istisna 1): People, Scheduling ve Billing tablolarını açık sorgularla
// okur, hiçbirine yazmaz. Ölçek küçük (~150 öğrenci, 6 ayda birkaç bin ders satırı) - satırlar
// belleğe alınıp C#'ta toplanır; sorgular basit ve EF çevirisine takılmaz.
public static class ProfitabilitySnapshotBuilder
{
    public const int MonthsShown = 6;
    public const int UnmarkedLessonLookbackDays = 14;
    public const int ExpiringCreditDays = 10;
    private const decimal DaysPerMonth = 30.44m;

    public static async Task<ProfitSnapshot> BuildAsync(AbderaDbContext db, IClock clock, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var months = Enumerable.Range(0, MonthsShown).Select(index => currentMonth.AddMonths(index - MonthsShown + 1)).ToList();
        var periods = months.Select(month => BillingPeriod.Format(month.Year, month.Month)).ToList();
        var currentPeriod = periods[^1];
        var windowStart = LessonGenerator.ToUtcInstant(months[0], TimeOnly.MinValue, clock.SchoolTimeZone);
        var windowEnd = LessonGenerator.ToUtcInstant(currentMonth.AddMonths(1), TimeOnly.MinValue, clock.SchoolTimeZone);

        // --- Gelir: dönemin iptal edilmemiş aidatları (tahakkuk) ---
        var receivables = await db.Receivables.AsNoTracking()
            .Where(receivable => periods.Contains(receivable.Period) && receivable.Status != ReceivableStatus.Cancelled)
            .Select(receivable => new { receivable.Period, receivable.EnrollmentId, receivable.BaseAmount, receivable.Amount })
            .ToListAsync(cancellationToken);
        var revenueByPeriod = receivables.GroupBy(row => row.Period).ToDictionary(group => group.Key, group => group.Sum(row => row.Amount));
        var currentReceivables = receivables.Where(row => row.Period == currentPeriod).ToList();
        var currentBase = currentReceivables.Sum(row => row.BaseAmount);
        var averageDiscount = currentBase > 0 ? Math.Clamp(1 - currentReceivables.Sum(row => row.Amount) / currentBase, 0, 1) : 0;

        // --- Öğretmen maliyeti: ders satırı × ders başı ücret (TeacherPayouts'un saydığı birim) ---
        var activeTeachers = await db.Teachers.AsNoTracking()
            .Where(teacher => teacher.Status == TeacherStatus.Active)
            .Select(teacher => teacher.Id)
            .ToListAsync(cancellationToken);
        var rates = await db.TeacherPayRates.AsNoTracking()
            .ToDictionaryAsync(rate => rate.TeacherId, rate => rate.AmountPerLesson, cancellationToken);
        var knownRates = activeTeachers.Where(rates.ContainsKey).Select(id => rates[id]).ToList();
        decimal? averageRate = knownRates.Count > 0 ? decimal.Round(knownRates.Average(), 2) : null;

        var lessons = await db.Lessons.AsNoTracking()
            .Where(lesson => lesson.StartAt >= windowStart && lesson.StartAt < windowEnd)
            .Select(lesson => new { lesson.TeacherId, lesson.StartAt, lesson.EndAt, lesson.Status })
            .ToListAsync(cancellationToken);

        // --- Sabit gider: tekrarlayan kalemler + tek seferlik giderler (öğretmen ödemesi satırları hariç) ---
        var recurring = await db.RecurringExpenses.AsNoTracking().Include(item => item.Amounts).ToListAsync(cancellationToken);
        var payoutExpenseIds = await db.TeacherWeeklyPayouts.AsNoTracking().Select(payout => payout.ExpenseId).ToListAsync(cancellationToken);
        var oneOffs = await db.Expenses.AsNoTracking()
            .Where(expense => expense.ExpenseDate >= months[0] && expense.ExpenseDate < currentMonth.AddMonths(1)
                && !payoutExpenseIds.Contains(expense.Id))
            .Select(expense => new { expense.ExpenseDate, expense.Amount })
            .ToListAsync(cancellationToken);

        var monthRows = new List<MonthProfit>();
        foreach (var (month, index) in months.Select((month, index) => (month, index)))
        {
            var from = LessonGenerator.ToUtcInstant(month, TimeOnly.MinValue, clock.SchoolTimeZone);
            var to = LessonGenerator.ToUtcInstant(month.AddMonths(1), TimeOnly.MinValue, clock.SchoolTimeZone);
            // Geçmiş ders yalnızca tamamlandıysa ödenir; bu ayın henüz gelmemiş dersleri planlandığı
            // gibi sayılır ki ay ortasında maliyet sıfıra yakın görünüp kâr şişmesin.
            var paidLessons = lessons.Where(lesson => lesson.StartAt >= from && lesson.StartAt < to
                && (lesson.Status == LessonStatus.Completed
                    || (lesson.EndAt > now && lesson.Status is LessonStatus.Normal or LessonStatus.Makeup)));
            decimal teacherCost = 0;
            var estimated = false;
            foreach (var lesson in paidLessons)
            {
                if (rates.TryGetValue(lesson.TeacherId, out var rate)) teacherCost += rate;
                else { teacherCost += averageRate ?? 0; estimated = true; }
            }
            var fixedCost = recurring.Sum(item => item.AmountFor(month))
                + oneOffs.Where(expense => expense.ExpenseDate >= month && expense.ExpenseDate < month.AddMonths(1)).Sum(expense => expense.Amount);
            monthRows.Add(new MonthProfit(
                periods[index], revenueByPeriod.GetValueOrDefault(periods[index]), decimal.Round(teacherCost, 2), fixedCost,
                index == months.Count - 1, estimated));
        }

        // --- Tarife ---
        var tariffs = await db.TuitionRates.AsNoTracking()
            .Where(rate => rate.EffectiveFrom <= today && (rate.EffectiveUntil == null || rate.EffectiveUntil >= today))
            .ToListAsync(cancellationToken);
        var individualTariff = tariffs.FirstOrDefault(rate => rate.CourseKind == CourseKind.Individual);
        var groupTariff = tariffs.FirstOrDefault(rate => rate.CourseKind == CourseKind.Group);

        // --- Kayıtlar ve kalış süresi ---
        var enrollments = await db.Enrollments.AsNoTracking()
            .Select(enrollment => new
            {
                enrollment.Id, enrollment.TeacherId, enrollment.InstrumentId, enrollment.CourseKind,
                enrollment.Status, enrollment.StartedAt, enrollment.EndedAt,
            })
            .ToListAsync(cancellationToken);
        var active = enrollments.Where(enrollment => enrollment.Status == EnrollmentStatus.Active).ToList();
        var ended = enrollments.Where(enrollment => enrollment.Status == EnrollmentStatus.Ended && enrollment.EndedAt is not null).ToList();
        var tenureIsDefault = ended.Count < ProfitBaseline.MinimumEndedForTenure;
        var tenure = tenureIsDefault
            ? ProfitBaseline.DefaultTenureMonths
            : Math.Max(1, ended.Average(enrollment => (enrollment.EndedAt!.Value.DayNumber - enrollment.StartedAt.DayNumber) / DaysPerMonth));
        var recentlyEnded = ended.Where(enrollment => enrollment.EndedAt >= today.AddMonths(-3)).ToList();
        var earlyChurn = recentlyEnded.Count(enrollment => enrollment.EndedAt!.Value.DayNumber - enrollment.StartedAt.DayNumber < 92);

        // --- Branş doluluğu: haftalık dolu saat / müsaitlik ---
        var activeEnrollmentIds = active.Select(enrollment => enrollment.Id).ToList();
        var series = await db.LessonSeries.AsNoTracking()
            .Where(item => item.Status == LessonSeriesStatus.Active && activeEnrollmentIds.Contains(item.EnrollmentId)
                && (item.EffectiveUntil == null || item.EffectiveUntil >= today))
            .Select(item => new { item.EnrollmentId, item.DayOfWeek, item.StartTime, item.DurationMinutes })
            .ToListAsync(cancellationToken);
        var enrollmentById = active.ToDictionary(enrollment => enrollment.Id);
        // Aynı öğretmenin aynı saatteki grup dersi tek saat sayılır.
        var slots = series
            .Select(item => new
            {
                enrollmentById[item.EnrollmentId].TeacherId,
                enrollmentById[item.EnrollmentId].InstrumentId,
                item.DayOfWeek, item.StartTime,
                Hours = item.DurationMinutes / 60m,
            })
            .DistinctBy(slot => (slot.TeacherId, slot.DayOfWeek, slot.StartTime))
            .ToList();
        var bookedByTeacher = slots.GroupBy(slot => slot.TeacherId).ToDictionary(group => group.Key, group => group.Sum(slot => slot.Hours));

        var availability = await db.TeacherAvailabilities.AsNoTracking()
            .Select(item => new { item.TeacherId, item.StartTime, item.EndTime })
            .ToListAsync(cancellationToken);
        var availableByTeacher = availability.GroupBy(item => item.TeacherId)
            .ToDictionary(group => group.Key, group => group.Sum(item => (decimal)(item.EndTime - item.StartTime).TotalHours));

        var teacherInstruments = await db.TeacherInstruments.AsNoTracking()
            .Where(link => activeTeachers.Contains(link.TeacherId))
            .Select(link => new { link.TeacherId, link.InstrumentId })
            .ToListAsync(cancellationToken);
        var instruments = await db.Instruments.AsNoTracking().OrderBy(instrument => instrument.Name).ToListAsync(cancellationToken);

        var instrumentRows = instruments.Select(instrument =>
        {
            // Künyedeki enstrümanlar + fiilen ders verdiği enstrümanlar.
            var teacherIds = teacherInstruments.Where(link => link.InstrumentId == instrument.Id).Select(link => link.TeacherId)
                .Concat(active.Where(enrollment => enrollment.InstrumentId == instrument.Id).Select(enrollment => enrollment.TeacherId))
                .Where(activeTeachers.Contains)
                .Distinct().ToList();
            var booked = slots.Where(slot => slot.InstrumentId == instrument.Id).Sum(slot => slot.Hours);
            // Birden çok branş veren öğretmenin boş saati her branşa açıktır: branşın kapasitesi =
            // kendi dolu saati + öğretmenlerinin toplam boş saati.
            var measured = teacherIds.Where(availableByTeacher.ContainsKey).ToList();
            decimal? available = measured.Count == 0 ? null
                : booked + measured.Sum(id => Math.Max(0, availableByTeacher[id] - bookedByTeacher.GetValueOrDefault(id)));
            var instrumentRates = teacherIds.Where(rates.ContainsKey).Select(id => rates[id]).ToList();
            return new InstrumentStats(
                instrument.Id, instrument.Name,
                active.Count(enrollment => enrollment.InstrumentId == instrument.Id && enrollment.CourseKind == CourseKind.Individual),
                active.Count(enrollment => enrollment.InstrumentId == instrument.Id && enrollment.CourseKind == CourseKind.Group),
                teacherIds.Count,
                instrumentRates.Count > 0 ? decimal.Round(instrumentRates.Average(), 2) : null,
                booked,
                available);
        }).ToList();

        var baseline = new ProfitBaseline(
            individualTariff?.MonthlyAmount ?? 0,
            groupTariff?.MonthlyAmount ?? 0,
            individualTariff?.LessonsPerMonth ?? groupTariff?.LessonsPerMonth ?? 4,
            decimal.Round(averageDiscount, 4),
            averageRate,
            monthRows[^1].Revenue,
            active.Count,
            decimal.Round(tenure, 1),
            tenureIsDefault,
            instrumentRows);

        // --- İşleyiş eksikleri ---
        var gaps = new List<OperationGap>();
        var teachersWithEnrollments = active.Select(enrollment => enrollment.TeacherId).Where(activeTeachers.Contains).Distinct().ToList();

        var withoutRate = teachersWithEnrollments.Count(id => !rates.ContainsKey(id));
        if (withoutRate > 0)
            gaps.Add(new OperationGap("teachers_without_rate", GapSeverity.Warning, withoutRate, averageRate is null
                ? $"{withoutRate} öğretmenin ders ücreti girilmemiş. Öğretmen maliyeti sıfır sayıldı, kâr olduğundan yüksek görünüyor."
                : $"{withoutRate} öğretmenin ders ücreti girilmemiş; onların dersleri ortalama ücretle ({Tl.Format(averageRate.Value)}) hesaplandı."));

        foreach (var kind in active.Select(enrollment => enrollment.CourseKind).Distinct())
        {
            if (tariffs.Any(rate => rate.CourseKind == kind)) continue;
            gaps.Add(new OperationGap($"missing_tariff_{kind.ToString().ToLowerInvariant()}", GapSeverity.Warning, 1,
                $"{(kind == CourseKind.Group ? "Grup" : "Birebir")} dersi için geçerli bir fiyat yok; bu öğrencilerin aidatı açılamıyor."));
        }

        var lookback = now.AddDays(-UnmarkedLessonLookbackDays);
        var unmarked = await db.Lessons.AsNoTracking()
            .CountAsync(lesson => lesson.StartAt >= lookback && lesson.EndAt < now
                && (lesson.Status == LessonStatus.Normal || lesson.Status == LessonStatus.Makeup), cancellationToken);
        if (unmarked > 0)
            gaps.Add(new OperationGap("unmarked_lessons", GapSeverity.Warning, unmarked,
                $"Son {UnmarkedLessonLookbackDays} günde {unmarked} dersin yoklaması alınmamış; bu dersler öğretmen ödemesinde ve kâr hesabında görünmüyor."));

        var billedThisMonth = currentReceivables.Select(row => row.EnrollmentId).ToHashSet();
        var withoutReceivable = active.Count(enrollment => enrollment.StartedAt <= today && !billedThisMonth.Contains(enrollment.Id));
        if (withoutReceivable > 0)
            gaps.Add(new OperationGap("enrollments_without_receivable", GapSeverity.Warning, withoutReceivable,
                $"{withoutReceivable} öğrencinin bu ayki aidatı açılmamış; gelir eksik görünüyor."));

        var scheduled = series.Select(item => item.EnrollmentId).ToHashSet();
        var withoutSchedule = active.Count(enrollment => !scheduled.Contains(enrollment.Id));
        if (withoutSchedule > 0)
            gaps.Add(new OperationGap("enrollments_without_schedule", GapSeverity.Info, withoutSchedule,
                $"{withoutSchedule} öğrencinin haftalık ders saati belirlenmemiş; aidat açılıyor ama ders takvime düşmüyor."));

        var withoutAvailability = teachersWithEnrollments.Count(id => !availableByTeacher.ContainsKey(id));
        if (withoutAvailability > 0)
            gaps.Add(new OperationGap("teachers_without_availability", GapSeverity.Info, withoutAvailability,
                $"{withoutAvailability} öğretmenin çalışma saatleri girilmemiş; boş saatler hesaplanamıyor."));

        var creditLimit = now.AddDays(ExpiringCreditDays);
        var expiring = await db.MakeupCredits.AsNoTracking()
            .CountAsync(credit => credit.Status == MakeupCreditStatus.Available && credit.ExpiresAt >= now && credit.ExpiresAt < creditLimit, cancellationToken);
        if (expiring > 0)
            gaps.Add(new OperationGap("expiring_makeup_credits", GapSeverity.Info, expiring,
                $"{expiring} telafi hakkının süresi {ExpiringCreditDays} gün içinde doluyor; telafi dersi planlanmazsa veli memnuniyeti düşer."));

        if (tenureIsDefault)
            gaps.Add(new OperationGap("tenure_default", GapSeverity.Info, ended.Count,
                $"Ayrılan öğrenci verisi henüz az; bir öğrencinin ortalama {ProfitBaseline.DefaultTenureMonths:0} ay kaldığı varsayıldı."));

        return new ProfitSnapshot(currentPeriod, monthRows, baseline, recentlyEnded.Count, earlyChurn, gaps);
    }
}
