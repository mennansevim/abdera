using System.Security.Claims;
using Abdera.Api.Modules.Dashboard.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Dashboard.Features;

// Öğretmen analizi ekranı (/dashboard/teachers/analysis): öğrenci analiziyle aynı aylık şerit,
// öğretmenin dersleri üzerinden - ders yükü, öğrencilerinin devamı, ders notu girme oranı,
// çalıştırdığı eserlerin zorluğu ve kendisine yapılan ödemeler.
//
// Yetki: Admin her öğretmeni görür; öğretmen yalnızca kendini (başka bir öğretmenin
// kazancını ve öğrenci listesini görmemeli). Ödemeler yalnızca okunur - tutarın hesap yeri
// TeacherPayRate/GroupLessonPay, burada yalnızca donmuş ödeme satırları toplanır.
public static class TeacherAnalysis
{
    public record MonthItem(
        string Period,
        int Present, int Makeup, int Excused, int Absent, int Cancelled, int Unmarked, int Upcoming,
        int Completed,
        int? AttendanceRate,
        int ActiveStudents,
        int NotedLessons, int? NoteCoverage,
        decimal? AverageDifficulty,
        decimal Paid);

    public record StudentRow(
        Guid StudentId, string Name, string Instruments,
        int Lessons, int Attended, int Rated, int? AttendanceRate, int Unmarked,
        DateTimeOffset? LastLessonAt, DateTimeOffset? LastNoteAt, decimal? AverageDifficulty);

    // Day: ISO hafta günü (1 = Pazartesi … 7 = Pazar). Lessons iptaller hariç.
    public record WeekdayItem(int Day, int Lessons, int Attended, int Rated);

    public record Response(
        Guid TeacherId, string FullName, TeacherStatus Status, List<string> Instruments,
        int ActiveStudentCount, string Currency,
        List<MonthItem> Months, List<StudentRow> Students, List<WeekdayItem> Weekdays);

    public static void MapTeacherAnalysis(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/teachers/{teacherId:guid}/analysis", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> HandleAsync(
        Guid teacherId, int? months, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var teacher = await db.Teachers.AsNoTracking().SingleOrDefaultAsync(t => t.Id == teacherId)
            ?? throw new NotFoundException("Öğretmen bulunamadı.");
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is { } scopedTeacherId && scopedTeacherId != teacherId)
            throw new ForbiddenException("Yalnızca kendi analizinizi görebilirsiniz.");

        var now = clock.UtcNow;
        var todayLocal = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);
        var periods = AnalysisTimeline.Periods(todayLocal, AnalysisTimeline.NormalizeMonths(months));
        var firstDay = AnalysisTimeline.FirstDay(periods[0]);
        var endDay = AnalysisTimeline.FirstDay(periods[^1]).AddMonths(1);
        var rangeStart = LessonGenerator.ToUtcInstant(firstDay, TimeOnly.MinValue, clock.SchoolTimeZone);
        var rangeEnd = LessonGenerator.ToUtcInstant(endDay, TimeOnly.MinValue, clock.SchoolTimeZone);
        string PeriodOfInstant(DateTimeOffset instant) =>
            AnalysisTimeline.PeriodOf(DateOnly.FromDateTime(clock.ToSchoolLocal(instant).Date));

        var lessons = await db.Lessons.AsNoTracking()
            .Where(l => l.TeacherId == teacherId && l.StartAt >= rangeStart && l.StartAt < rangeEnd &&
                        l.Status != LessonStatus.Rescheduled)
            .Select(l => new { l.Id, l.StartAt, l.Status, l.StudentId, l.InstrumentId })
            .ToListAsync();
        var lessonIds = lessons.Select(l => l.Id).ToList();
        var attendance = await db.LessonAttendances.AsNoTracking()
            .Where(a => lessonIds.Contains(a.LessonId))
            .ToDictionaryAsync(a => a.LessonId, a => a.Status);
        var notes = await db.LessonNotes.AsNoTracking()
            .Where(n => lessonIds.Contains(n.LessonId))
            .Select(n => new { n.LessonId, n.PieceDifficulty })
            .ToListAsync();
        var notedLessonIds = notes.Select(n => n.LessonId).ToHashSet();

        var rows = lessons.Select(l => new
        {
            l.Id, l.StartAt, l.Status, l.StudentId, l.InstrumentId,
            Period = PeriodOfInstant(l.StartAt),
            State = AnalysisTimeline.Classify(l.Status, attendance.TryGetValue(l.Id, out var status) ? status : null, l.StartAt, now),
        }).ToList();

        var weeklyPayouts = await db.TeacherWeeklyPayouts.AsNoTracking()
            .Where(p => p.TeacherId == teacherId && p.PaidOn >= firstDay && p.PaidOn < endDay)
            .Select(p => new { p.PaidOn, p.Amount, p.Currency })
            .ToListAsync();
        var groupPayouts = await db.TeacherMonthlyGroupPayouts.AsNoTracking()
            .Where(p => p.TeacherId == teacherId && periods.Contains(p.Period))
            .Select(p => new { p.Period, p.Amount, p.Currency })
            .ToListAsync();

        var monthItems = periods.Select(period =>
        {
            var month = rows.Where(r => r.Period == period).ToList();
            int Count(AnalysisLessonState state) => month.Count(r => r.State == state);
            var attended = month.Where(r => AnalysisTimeline.IsAttended(r.State)).ToList();
            var rated = month.Count(r => AnalysisTimeline.CountsTowardRate(r.State));
            var noted = attended.Count(r => notedLessonIds.Contains(r.Id));
            var monthLessonIds = month.Select(r => r.Id).ToHashSet();
            return new MonthItem(
                period,
                Count(AnalysisLessonState.Present), Count(AnalysisLessonState.Makeup),
                Count(AnalysisLessonState.Excused), Count(AnalysisLessonState.Absent),
                Count(AnalysisLessonState.Cancelled), Count(AnalysisLessonState.Unmarked),
                Count(AnalysisLessonState.Upcoming),
                month.Count(r => r.Status == LessonStatus.Completed),
                AnalysisTimeline.Rate(attended.Count, rated),
                month.Where(r => r.State != AnalysisLessonState.Cancelled).Select(r => r.StudentId).Distinct().Count(),
                noted, AnalysisTimeline.Rate(noted, attended.Count),
                AnalysisTimeline.Average(notes
                    .Where(n => monthLessonIds.Contains(n.LessonId) && n.PieceDifficulty is not null)
                    .Select(n => n.PieceDifficulty!.Value)),
                weeklyPayouts.Where(p => AnalysisTimeline.PeriodOf(p.PaidOn) == period).Sum(p => p.Amount) +
                groupPayouts.Where(p => p.Period == period).Sum(p => p.Amount));
        }).ToList();

        var activeEnrollments = await db.Enrollments.AsNoTracking()
            .Where(e => e.TeacherId == teacherId && e.Status == EnrollmentStatus.Active)
            .Select(e => new { e.StudentId, e.InstrumentId })
            .ToListAsync();

        // Liste: aralıkta dersi olan + şu an aktif kaydı olan öğrenciler (yeni kayıt, henüz dersi
        // yok diye listeden düşmesin).
        var studentIds = rows.Select(r => r.StudentId).Concat(activeEnrollments.Select(e => e.StudentId)).Distinct().ToList();
        var students = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FirstName + " " + s.LastName);
        var instrumentIds = rows.Select(r => r.InstrumentId).Concat(activeEnrollments.Select(e => e.InstrumentId)).Distinct().ToList();
        var instrumentNames = await db.Instruments.AsNoTracking()
            .Where(i => instrumentIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Name);
        var teacherInstrumentIds = await db.TeacherInstruments.AsNoTracking()
            .Where(ti => ti.TeacherId == teacherId)
            .Select(ti => ti.InstrumentId)
            .ToListAsync();
        var teacherInstruments = await db.Instruments.AsNoTracking()
            .Where(i => teacherInstrumentIds.Contains(i.Id))
            .OrderBy(i => i.Name)
            .Select(i => i.Name)
            .ToListAsync();

        var difficultyByLesson = notes.Where(n => n.PieceDifficulty is not null)
            .GroupBy(n => n.LessonId)
            .ToDictionary(g => g.Key, g => g.Select(n => n.PieceDifficulty!.Value).ToList());

        var studentRows = studentIds
            .Where(students.ContainsKey)
            .Select(id =>
            {
                var own = rows.Where(r => r.StudentId == id).ToList();
                var held = own.Where(r => r.State != AnalysisLessonState.Cancelled && r.State != AnalysisLessonState.Upcoming).ToList();
                var attendedCount = own.Count(r => AnalysisTimeline.IsAttended(r.State));
                var ratedCount = own.Count(r => AnalysisTimeline.CountsTowardRate(r.State));
                var instruments = own.Select(r => r.InstrumentId)
                    .Concat(activeEnrollments.Where(e => e.StudentId == id).Select(e => e.InstrumentId))
                    .Distinct()
                    .Select(i => instrumentNames.GetValueOrDefault(i) ?? "")
                    .Where(name => name.Length > 0)
                    .Order(StringComparer.Ordinal);
                var notedOwn = own.Where(r => notedLessonIds.Contains(r.Id)).ToList();
                return new StudentRow(
                    id, students[id], string.Join(", ", instruments),
                    held.Count, attendedCount, ratedCount, AnalysisTimeline.Rate(attendedCount, ratedCount),
                    own.Count(r => r.State == AnalysisLessonState.Unmarked),
                    held.Count == 0 ? null : held.Max(r => r.StartAt),
                    notedOwn.Count == 0 ? null : notedOwn.Max(r => r.StartAt),
                    AnalysisTimeline.Average(own.SelectMany(r => difficultyByLesson.GetValueOrDefault(r.Id) ?? [])));
            })
            // Dikkat isteyen önce: en düşük devam oranı üstte, oranı olmayanlar en sonda.
            .OrderBy(row => row.AttendanceRate ?? int.MaxValue)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var weekdays = rows
            .Where(r => r.State != AnalysisLessonState.Cancelled)
            .GroupBy(r => IsoDay(clock.ToSchoolLocal(r.StartAt).DayOfWeek))
            .OrderBy(g => g.Key)
            .Select(g => new WeekdayItem(
                g.Key, g.Count(),
                g.Count(r => AnalysisTimeline.IsAttended(r.State)),
                g.Count(r => AnalysisTimeline.CountsTowardRate(r.State))))
            .ToList();

        var currency = weeklyPayouts.Select(p => p.Currency).Concat(groupPayouts.Select(p => p.Currency)).FirstOrDefault() ?? "TRY";

        return Results.Ok(new Response(
            teacher.Id, teacher.FirstName + " " + teacher.LastName, teacher.Status, teacherInstruments,
            activeEnrollments.Select(e => e.StudentId).Distinct().Count(), currency,
            monthItems, studentRows, weekdays));
    }

    private static int IsoDay(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;
}
