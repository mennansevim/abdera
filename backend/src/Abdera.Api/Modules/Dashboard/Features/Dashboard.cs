using System.Security.Claims;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Dashboard.Features;

// docs/00-master-prompt.md "Dashboard" bölümü + docs/07-api.md GET /api/dashboard/today
// örnek yanıtı (denetim ARC-6/E2, docs/13-audit-fix-prompt.md madde 13). docs/02-modules.md
// İstisna 1: Dashboard salt-okunur olduğu için kendi tablosu yok, başka modüllerin
// tablolarını doğrudan AbderaDbContext üzerinden açık LINQ sorgularıyla okuyabilir
// (navigation property değil). "Do not turn the dashboard into a BI project" - tek uç
// nokta, tek sorgu seti.
//
// Rol bazlı davranış (docs/04-permissions.md): Admin okul geneli görür. Teacher yalnızca
// kendi dersleri üzerinden hesaplanan sayıları görür; mali alanlar (overduePayments) Teacher
// için her zaman 0'dır - "Aidat/tahsilat/okul geneli mali özet" tamamen Admin'e ait.
public static class Dashboard
{
    private const int UpcomingWindowDays = 30;

    public record TodayResponse(
        int TodayLessons, int Attending, int NotAttending, int NoResponse,
        int PendingChangeRequests, int OverduePayments, int UpcomingBirthdays, int UpcomingSchoolEvents);

    public static void MapDashboard(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/today", GetTodayAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> GetTodayAsync(ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        var todayLocal = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var todayStartUtc = LessonGenerator.ToUtcInstant(todayLocal, TimeOnly.MinValue, clock.SchoolTimeZone);
        var tomorrowStartUtc = LessonGenerator.ToUtcInstant(todayLocal.AddDays(1), TimeOnly.MinValue, clock.SchoolTimeZone);

        var todaysLessonsQuery = db.Lessons.Where(l =>
            l.StartAt >= todayStartUtc && l.StartAt < tomorrowStartUtc && l.Status != LessonStatus.Cancelled);
        if (teacherScope is { } scopedTeacherId)
        {
            todaysLessonsQuery = todaysLessonsQuery.Where(l => l.TeacherId == scopedTeacherId);
        }
        var todaysLessonIds = await todaysLessonsQuery.Select(l => l.Id).ToListAsync();

        var (attending, notAttending, noResponse) = await SummarizeRsvpsAsync(todaysLessonIds, db);

        var pendingChangeRequestsQuery = db.LessonChangeRequests
            .Where(c => c.Status == LessonChangeRequestStatus.Pending);
        if (teacherScope is { } scopedTeacherIdForChanges)
        {
            pendingChangeRequestsQuery = pendingChangeRequestsQuery
                .Join(db.Lessons.Where(l => l.TeacherId == scopedTeacherIdForChanges), c => c.LessonId, l => l.Id, (c, _) => c);
        }
        var pendingChangeRequests = await pendingChangeRequestsQuery.CountAsync();

        // Mali özet tamamen Admin'e ait (docs/04-permissions.md) - Teacher için sorgu bile
        // çalıştırılmaz, doğrudan 0.
        var overduePayments = teacherScope is null
            ? await db.Receivables.CountAsync(r => r.Status == ReceivableStatus.Overdue)
            : 0;

        var upcomingBirthdays = await CountUpcomingBirthdaysAsync(teacherScope, todayLocal, db);

        // Okul takvimi kurum geneli - rol bazlı kapsam farkı yok.
        var upcomingSchoolEvents = await db.SchoolCalendarDays.CountAsync(d =>
            d.Type == SchoolCalendarDayType.Event && d.Date >= todayLocal && d.Date <= todayLocal.AddDays(UpcomingWindowDays));

        return Results.Ok(new TodayResponse(
            todaysLessonIds.Count, attending, notAttending, noResponse,
            pendingChangeRequests, overduePayments, upcomingBirthdays, upcomingSchoolEvents));
    }

    // Bir ders birden fazla veliye bağlı olabilir (UNIQUE(lesson_id, guardian_id)) - en az bir
    // veli "geliyorum" dediyse Attending, hiçbiri gelmiyorsa NotAttending, hiç RSVP yoksa
    // (veya yalnızca Unknown) NoResponse sayılır. todayLessons = attending+notAttending+noResponse.
    private static async Task<(int Attending, int NotAttending, int NoResponse)> SummarizeRsvpsAsync(
        List<Guid> lessonIds, AbderaDbContext db)
    {
        if (lessonIds.Count == 0) return (0, 0, 0);

        var rsvpSummaries = await db.LessonRsvps
            .Where(r => lessonIds.Contains(r.LessonId))
            .GroupBy(r => r.LessonId)
            .Select(g => new
            {
                LessonId = g.Key,
                HasAttending = g.Any(r => r.Response == RsvpResponse.Attending),
                HasNotAttending = g.Any(r => r.Response == RsvpResponse.NotAttending),
            })
            .ToListAsync();
        var byLesson = rsvpSummaries.ToDictionary(x => x.LessonId);

        int attending = 0, notAttending = 0, noResponse = 0;
        foreach (var lessonId in lessonIds)
        {
            if (byLesson.TryGetValue(lessonId, out var summary) && summary.HasAttending) attending++;
            else if (byLesson.TryGetValue(lessonId, out var summary2) && summary2.HasNotAttending) notAttending++;
            else noResponse++;
        }

        return (attending, notAttending, noResponse);
    }

    // Doğum günleri ay/gün bazlı tekrar eder (yıl bağımsız) - EF/SQL'e böyle bir karşılaştırma
    // temiz çevrilmediğinden (ve bu ölçekte en fazla ~150 öğrenci var, "BI projesi" olmasın
    // diye) doğum tarihleri belleğe çekilip C# tarafında hesaplanıyor.
    private static async Task<int> CountUpcomingBirthdaysAsync(Guid? teacherScope, DateOnly today, AbderaDbContext db)
    {
        var birthDatesQuery = db.Students.Where(s => s.Status == StudentStatus.Active).Select(s => s.BirthDate);
        if (teacherScope is { } scopedTeacherId)
        {
            var studentIds = db.Enrollments
                .Where(e => e.TeacherId == scopedTeacherId && e.Status == EnrollmentStatus.Active)
                .Select(e => e.StudentId);
            birthDatesQuery = db.Students.Where(s => s.Status == StudentStatus.Active && studentIds.Contains(s.Id)).Select(s => s.BirthDate);
        }

        var birthDates = await birthDatesQuery.ToListAsync();
        return birthDates.Count(birthDate => IsBirthdayWithinWindow(birthDate, today, UpcomingWindowDays));
    }

    private static bool IsBirthdayWithinWindow(DateOnly birthDate, DateOnly today, int windowDays)
    {
        var day = birthDate is { Month: 2, Day: 29 } && !DateTime.IsLeapYear(today.Year) ? 28 : birthDate.Day;
        var nextOccurrence = new DateOnly(today.Year, birthDate.Month, day);
        if (nextOccurrence < today)
        {
            var nextYear = today.Year + 1;
            var dayNextYear = birthDate is { Month: 2, Day: 29 } && !DateTime.IsLeapYear(nextYear) ? 28 : birthDate.Day;
            nextOccurrence = new DateOnly(nextYear, birthDate.Month, dayNextYear);
        }

        return nextOccurrence <= today.AddDays(windowDays);
    }
}
