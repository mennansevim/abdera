using System.Security.Claims;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Billing.Features;
using Abdera.Api.Modules.Dashboard.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Dashboard.Features;

// Öğrenci analizi ekranı (/dashboard/students/analysis): devam, seviye, ödev ve aidat aynı aylık
// şeritte. Dashboard modülünün salt-okunur istisnası (docs/02-modules.md İstisna 1) - başka
// modüllerin tablolarını açık sorgularla okur, kendi tablosu yok.
//
// Yetki (docs/04-permissions.md): Admin her öğrenciyi, öğretmen yalnızca kendisine kurs kaydıyla
// bağlı öğrenciyi görür. Öğretmen öğrencinin akademik resminin tamamını (diğer öğretmenlerin
// dersleri ve notları dahil) görür; aidat ve bakiye ise Admin'e aittir - öğretmen için o
// sorgular hiç çalışmaz, alanlar null döner.
public static class StudentAnalysis
{
    public record LessonDot(DateTimeOffset StartAt, AnalysisLessonState State, string TeacherName);

    public record BillingMonth(
        decimal Amount, decimal Paid, string Currency, ReceivableStatus Status, DateOnly DueDate,
        string? DiscountReason, DateOnly? LastPaymentDate);

    public record MonthNote(DateTimeOffset At, string TeacherName, string? Text, string? NextGoal);

    public record MonthItem(
        string Period,
        List<LessonDot> Lessons,
        int Attended, int Rated, int? AttendanceRate, int Cancelled, int Unmarked,
        decimal? AverageDifficulty,
        int AssignmentCount, int AssignmentCompleted, int? AssignmentRate,
        int NoteCount,
        BillingMonth? Billing,
        MonthNote? LastNote);

    public record PieceItem(string Title, string? Composer, int? Difficulty, string FirstPeriod, string LastPeriod, bool Finished);

    // Day: ISO hafta günü (1 = Pazartesi … 7 = Pazar).
    public record WeekdayItem(int Day, int Present, int Makeup, int Excused, int Absent);

    public record CourseItem(
        Guid EnrollmentId, string InstrumentName, string TeacherName, CourseKind CourseKind,
        EnrollmentStatus Status, DateOnly StartedAt);

    public record GuardianItem(string Name, string? Relationship, string PhoneNumber, bool NotificationConsent);

    public record Response(
        Guid StudentId, string FullName, DateOnly BirthDate, StudentStatus Status, bool SiblingDiscount,
        DateOnly? EnrolledSince,
        List<CourseItem> Courses,
        List<GuardianItem> Guardians,
        int AvailableMakeupCredits, DateTimeOffset? NextMakeupCreditExpiry,
        bool BillingVisible, decimal? OpenBalance,
        List<MonthItem> Months,
        List<PieceItem> Pieces,
        List<WeekdayItem> Weekdays);

    public static void MapStudentAnalysis(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/students/{studentId:guid}/analysis", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> HandleAsync(
        Guid studentId, int? months, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var student = await db.Students.AsNoTracking().SingleOrDefaultAsync(s => s.Id == studentId)
            ?? throw new NotFoundException("Öğrenci bulunamadı.");

        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        var enrollments = await db.Enrollments.AsNoTracking().Where(e => e.StudentId == studentId).ToListAsync();
        if (teacherScope is { } scopedTeacherId && enrollments.All(e => e.TeacherId != scopedTeacherId))
            throw new ForbiddenException("Bu öğrenci size atanmamış.");
        var billingVisible = teacherScope is null;

        var now = clock.UtcNow;
        var todayLocal = DateOnly.FromDateTime(clock.ToSchoolLocal(now).Date);
        var periods = AnalysisTimeline.Periods(todayLocal, AnalysisTimeline.NormalizeMonths(months));
        var rangeStart = LessonGenerator.ToUtcInstant(AnalysisTimeline.FirstDay(periods[0]), TimeOnly.MinValue, clock.SchoolTimeZone);
        var rangeEnd = LessonGenerator.ToUtcInstant(AnalysisTimeline.FirstDay(periods[^1]).AddMonths(1), TimeOnly.MinValue, clock.SchoolTimeZone);
        string PeriodOfInstant(DateTimeOffset instant) =>
            AnalysisTimeline.PeriodOf(DateOnly.FromDateTime(clock.ToSchoolLocal(instant).Date));

        var lessons = await db.Lessons.AsNoTracking()
            .Where(l => l.StudentId == studentId && l.StartAt >= rangeStart && l.StartAt < rangeEnd &&
                        l.Status != LessonStatus.Rescheduled)
            .OrderBy(l => l.StartAt)
            .Select(l => new { l.Id, l.StartAt, l.Status, l.TeacherId })
            .ToListAsync();
        var lessonIds = lessons.Select(l => l.Id).ToList();

        var attendance = await db.LessonAttendances.AsNoTracking()
            .Where(a => lessonIds.Contains(a.LessonId))
            .ToDictionaryAsync(a => a.LessonId, a => a.Status);
        var notes = await db.LessonNotes.AsNoTracking()
            .Where(n => lessonIds.Contains(n.LessonId))
            .ToListAsync();
        var assignments = await db.PracticeAssignments.AsNoTracking()
            .Where(a => lessonIds.Contains(a.LessonId))
            .Select(a => new { a.LessonId, a.Completed })
            .ToListAsync();

        var teacherIds = lessons.Select(l => l.TeacherId)
            .Concat(notes.Select(n => n.TeacherId))
            .Concat(enrollments.Select(e => e.TeacherId))
            .Distinct().ToList();
        var teacherNames = await db.Teachers.AsNoTracking()
            .Where(t => teacherIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.FirstName + " " + t.LastName);
        string TeacherName(Guid id) => teacherNames.GetValueOrDefault(id) ?? "";

        var instrumentIds = enrollments.Select(e => e.InstrumentId).Distinct().ToList();
        var instrumentNames = await db.Instruments.AsNoTracking()
            .Where(i => instrumentIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Name);

        var lessonById = lessons.ToDictionary(l => l.Id);
        var states = lessons.ToDictionary(
            l => l.Id,
            l => AnalysisTimeline.Classify(l.Status, attendance.TryGetValue(l.Id, out var status) ? status : null, l.StartAt, now));

        var billing = billingVisible
            ? await LoadBillingAsync(enrollments.Select(e => e.Id).ToList(), periods, db)
            : null;

        var monthItems = periods.Select(period =>
        {
            var monthLessons = lessons.Where(l => PeriodOfInstant(l.StartAt) == period).ToList();
            var monthStates = monthLessons.Select(l => states[l.Id]).ToList();
            var attended = monthStates.Count(AnalysisTimeline.IsAttended);
            var rated = monthStates.Count(AnalysisTimeline.CountsTowardRate);
            var monthLessonIds = monthLessons.Select(l => l.Id).ToHashSet();
            var monthNotes = notes.Where(n => monthLessonIds.Contains(n.LessonId))
                .OrderBy(n => lessonById[n.LessonId].StartAt)
                .ToList();
            var monthAssignments = assignments.Where(a => monthLessonIds.Contains(a.LessonId)).ToList();
            var lastNote = monthNotes.LastOrDefault(n => n.Note is not null || n.Practiced is not null);

            return new MonthItem(
                period,
                monthLessons.Select(l => new LessonDot(l.StartAt, states[l.Id], TeacherName(l.TeacherId))).ToList(),
                attended, rated, AnalysisTimeline.Rate(attended, rated),
                monthStates.Count(s => s == AnalysisLessonState.Cancelled),
                monthStates.Count(s => s == AnalysisLessonState.Unmarked),
                AnalysisTimeline.Average(monthNotes.Where(n => n.PieceDifficulty is not null).Select(n => n.PieceDifficulty!.Value)),
                monthAssignments.Count, monthAssignments.Count(a => a.Completed),
                AnalysisTimeline.Rate(monthAssignments.Count(a => a.Completed), monthAssignments.Count),
                monthNotes.Count,
                billing?.GetValueOrDefault(period),
                lastNote is null
                    ? null
                    : new MonthNote(lessonById[lastNote.LessonId].StartAt, TeacherName(lastNote.TeacherId),
                        lastNote.Note ?? lastNote.Practiced, lastNote.NextGoal));
        }).ToList();

        // Repertuvar: aynı eser adı (büyük/küçük harf ve boşluk farkı gözetmeden) tek satır.
        // Son notun durumu "sahneye hazır" veya "arşiv" ise eser bitmiş sayılır.
        var pieces = notes
            .Where(n => !string.IsNullOrWhiteSpace(n.PieceTitle))
            .GroupBy(n => n.PieceTitle!.Trim().ToUpperInvariant())
            .Select(group =>
            {
                var ordered = group.OrderBy(n => lessonById[n.LessonId].StartAt).ToList();
                var latest = ordered[^1];
                return new
                {
                    FirstAt = lessonById[ordered[0].LessonId].StartAt,
                    Item = new PieceItem(
                        latest.PieceTitle!.Trim(),
                        ordered.LastOrDefault(n => n.PieceComposer is not null)?.PieceComposer,
                        ordered.Max(n => n.PieceDifficulty),
                        PeriodOfInstant(lessonById[ordered[0].LessonId].StartAt),
                        PeriodOfInstant(lessonById[latest.LessonId].StartAt),
                        latest.PieceStatus is RepertoireStatus.PerformanceReady or RepertoireStatus.Archived),
                };
            })
            .OrderBy(p => p.FirstAt)
            .Select(p => p.Item)
            .ToList();

        var weekdays = lessons
            .Select(l => new { Day = IsoDay(clock.ToSchoolLocal(l.StartAt).DayOfWeek), State = states[l.Id] })
            .Where(x => AnalysisTimeline.CountsTowardRate(x.State))
            .GroupBy(x => x.Day)
            .OrderBy(g => g.Key)
            .Select(g => new WeekdayItem(
                g.Key,
                g.Count(x => x.State == AnalysisLessonState.Present),
                g.Count(x => x.State == AnalysisLessonState.Makeup),
                g.Count(x => x.State == AnalysisLessonState.Excused),
                g.Count(x => x.State == AnalysisLessonState.Absent)))
            .ToList();

        var guardians = await db.StudentGuardians.AsNoTracking()
            .Where(link => link.StudentId == studentId)
            .Join(db.Guardians, link => link.GuardianId, g => g.Id, (link, g) => new { link, g })
            .OrderByDescending(x => x.link.IsPrimary)
            .ThenBy(x => x.g.FirstName)
            .Select(x => new GuardianItem(x.g.FirstName + " " + x.g.LastName, x.link.Relationship, x.g.PhoneNumber, x.g.NotificationConsent))
            .ToListAsync();

        var credits = await db.MakeupCredits.AsNoTracking()
            .Where(c => c.StudentId == studentId && c.Status == MakeupCreditStatus.Available && c.ExpiresAt > now)
            .Select(c => c.ExpiresAt)
            .ToListAsync();

        decimal? openBalance = billingVisible
            ? await ComputeOpenBalanceAsync(enrollments.Select(e => e.Id).ToList(), db)
            : null;

        var courses = enrollments
            .OrderBy(e => e.Status == EnrollmentStatus.Active ? 0 : 1)
            .ThenBy(e => e.StartedAt)
            .Select(e => new CourseItem(
                e.Id, instrumentNames.GetValueOrDefault(e.InstrumentId) ?? "", TeacherName(e.TeacherId),
                e.CourseKind, e.Status, e.StartedAt))
            .ToList();

        return Results.Ok(new Response(
            student.Id, student.FirstName + " " + student.LastName, student.BirthDate, student.Status, student.SiblingDiscount,
            enrollments.Count == 0 ? null : enrollments.Min(e => e.StartedAt),
            courses, guardians,
            credits.Count, credits.Count == 0 ? null : credits.Min(),
            billingVisible, openBalance,
            monthItems, pieces, weekdays));
    }

    private static int IsoDay(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    // Bir öğrencinin aynı ay birden fazla kursu olabilir: tutarlar toplanır, durum en kötüsü.
    private static async Task<Dictionary<string, BillingMonth>> LoadBillingAsync(
        List<Guid> enrollmentIds, IReadOnlyList<string> periods, AbderaDbContext db)
    {
        var receivables = await db.Receivables.AsNoTracking()
            .Where(r => enrollmentIds.Contains(r.EnrollmentId) && periods.Contains(r.Period) && r.Status != ReceivableStatus.Cancelled)
            .ToListAsync();
        var totals = await Receivables.ComputeTotalsPaidAsync(receivables.Select(r => r.Id), db);
        var payments = await Receivables.ComputePaymentsAsync(receivables.Select(r => r.Id), db);

        return receivables
            .GroupBy(r => r.Period)
            .ToDictionary(group => group.Key, group =>
            {
                var rows = group.ToList();
                var lastPayment = rows
                    .SelectMany(r => payments.GetValueOrDefault(r.Id) ?? [])
                    .Select(p => (DateOnly?)p.PaymentDate)
                    .Max();
                return new BillingMonth(
                    rows.Sum(r => r.Amount),
                    rows.Sum(r => totals.GetValueOrDefault(r.Id)),
                    rows[0].Currency,
                    AnalysisTimeline.CombineBillingStatus(rows.Select(r => r.Status)),
                    rows.Min(r => r.DueDate),
                    rows.Select(r => r.DiscountReason).FirstOrDefault(reason => reason is not null),
                    lastPayment);
            });
    }

    private static async Task<decimal> ComputeOpenBalanceAsync(List<Guid> enrollmentIds, AbderaDbContext db)
    {
        var open = await db.Receivables.AsNoTracking()
            .Where(r => enrollmentIds.Contains(r.EnrollmentId) &&
                        r.Status != ReceivableStatus.Cancelled && r.Status != ReceivableStatus.Paid)
            .Select(r => new { r.Id, r.Amount })
            .ToListAsync();
        var totals = await Receivables.ComputeTotalsPaidAsync(open.Select(r => r.Id), db);
        return open.Sum(r => Math.Max(0, r.Amount - totals.GetValueOrDefault(r.Id)));
    }
}
