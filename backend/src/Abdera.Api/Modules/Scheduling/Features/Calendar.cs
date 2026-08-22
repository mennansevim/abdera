using System.Security.Claims;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// docs/07-api.md GET /api/calendar, GET /api/lessons. docs/00-master-prompt.md Frontend UX:
// "Provide filters for All, Piano, Guitar, Drums, and individual teachers." docs/04-permissions.md:
// Admin okul genelini görür, Teacher yalnızca kendi derslerini.
public static class Calendar
{
    public record LessonResponse(
        Guid Id, DateTimeOffset StartAt, DateTimeOffset EndAt, LessonStatus Status,
        Guid StudentId, string StudentName, Guid TeacherId, string TeacherName,
        Guid InstrumentId, string InstrumentName, RsvpResponse? RsvpResponse);

    public static void MapCalendar(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/calendar", ListAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapGet("/api/lessons", ListAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    // ARC-3 (docs/13-audit-fix-prompt.md): bu uç noktada sayfalama yok (tek bir takvim
    // görünümü, doğal olarak sınırlı sayıda ders döner) - ama tarih aralığına hiç üst sınır
    // yoktu, bir yıllık ders geçmişi biriktiğinde sınırsız satır dönebilirdi.
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(93); // ~3 ay

    private static async Task<IResult> ListAsync(
        DateTimeOffset from, DateTimeOffset to, Guid? teacherId, Guid? instrumentId,
        ClaimsPrincipal principal, AbderaDbContext db)
    {
        if (to - from > MaxRange)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["to"] = ["Tarih aralığı en fazla 3 ay olabilir."],
            });
        }

        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);

        var query = db.Lessons.Where(l => l.StartAt >= from && l.StartAt < to);
        if (teacherScope is { } scopedTeacherId)
        {
            query = query.Where(l => l.TeacherId == scopedTeacherId);
        }
        else if (teacherId is { } filterTeacherId)
        {
            query = query.Where(l => l.TeacherId == filterTeacherId);
        }
        if (instrumentId is { } filterInstrumentId)
        {
            query = query.Where(l => l.InstrumentId == filterInstrumentId);
        }

        // Not: OrderBy, LessonResponse'a (record) projeksiyondan ÖNCE uygulanır - EF Core
        // bir record constructor'ının alanına göre sıralamayı SQL'e çeviremiyor
        // ("could not be translated" hatası). Sıralama anonim ara tipte yapılıp en son adımda
        // projekte edilir.
        var lessons = await query
            .Join(db.Students, l => l.StudentId, s => s.Id, (l, s) => new { Lesson = l, Student = s })
            .Join(db.Teachers, x => x.Lesson.TeacherId, t => t.Id, (x, t) => new { x.Lesson, x.Student, Teacher = t })
            .Join(db.Instruments, x => x.Lesson.InstrumentId, i => i.Id, (x, i) => new { x.Lesson, x.Student, x.Teacher, Instrument = i })
            .OrderBy(x => x.Lesson.StartAt)
            .Select(x => new LessonResponse(
                x.Lesson.Id, x.Lesson.StartAt, x.Lesson.EndAt, x.Lesson.Status,
                x.Student.Id, x.Student.FirstName + " " + x.Student.LastName,
                x.Teacher.Id, x.Teacher.FirstName + " " + x.Teacher.LastName,
                x.Instrument.Id, x.Instrument.Name, null))
            .ToListAsync();

        var lessonIds = lessons.Select(l => l.Id).ToList();
        var rsvpRows = await db.LessonRsvps
            .Where(r => lessonIds.Contains(r.LessonId))
            .GroupBy(r => r.LessonId)
            .Select(group => new
            {
                LessonId = group.Key,
                HasAttending = group.Any(r => r.Response == RsvpResponse.Attending),
                HasAttendingLate = group.Any(r => r.Response == RsvpResponse.AttendingLate),
                HasNotAttending = group.Any(r => r.Response == RsvpResponse.NotAttending),
            })
            .ToListAsync();
        // Birden fazla veli farklı cevap verirse en olumlu olan öne çıkar: Attending >
        // AttendingLate > NotAttending > Unknown (hiç yanıt yok).
        var rsvpByLesson = rsvpRows.ToDictionary(
            row => row.LessonId,
            row => row.HasAttending ? RsvpResponse.Attending
                : row.HasAttendingLate ? RsvpResponse.AttendingLate
                : row.HasNotAttending ? RsvpResponse.NotAttending
                : RsvpResponse.Unknown);

        return Results.Ok(lessons.Select(lesson => lesson with
        {
            RsvpResponse = rsvpByLesson.GetValueOrDefault(lesson.Id),
        }));
    }
}
