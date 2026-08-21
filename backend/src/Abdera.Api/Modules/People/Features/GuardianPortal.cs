using System.Security.Claims;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.People.Features;

// docs/10-decisions.md Karar F reversal: veli kendi öğrencisinin listesini, takvimini ve
// RSVP'sini görebilir/ayarlayabilir - GuardianAuth.cs ile kurulan oturuma bağlı. Salt-okunur
// aidat ve mesaj görünümü GuardianPortalData.cs'te ayrı bir read model olarak sunulur.
// URL'deki studentId/lessonId'ye asla güvenilmez - her handler önce StudentGuardians üzerinden
// çağıran velinin gerçekten o öğrenciye bağlı olduğunu doğrular (AuthContext'teki "Teacher
// isteğinde hedef kaynak oturumdan çözümlenir" ilkesinin Guardian karşılığı).
public static class GuardianPortal
{
    public record GuardianStudentResponse(Guid StudentId, string FirstName, string LastName, string? InstrumentName, string? TeacherName);

    public record GuardianLessonResponse(
        Guid Id, DateTimeOffset StartAt, DateTimeOffset EndAt, LessonStatus Status,
        string InstrumentName, string TeacherName, RsvpResponse RsvpResponse);

    public record SetRsvpRequest(RsvpResponse Response);
    public record SetRsvpResponse(Guid LessonId, RsvpResponse Response, DateTimeOffset RespondedAt);

    // Scheduling/Features/Calendar.cs'teki admin sorgusuyla aynı üst sınır - tek fark burada
    // teacherId/instrumentId filtresi yok, tek bir öğrenciye sabit.
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(93);

    public static void MapGuardianPortal(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/guardian/me").RequireAuthorization(AuthorizationPolicies.GuardianOnly);

        group.MapGet("/students", ListStudentsAsync);
        group.MapGet("/students/{studentId:guid}/calendar", CalendarAsync);
        group.MapPost("/lessons/{lessonId:guid}/rsvp", SetRsvpAsync);
    }

    private static async Task<IResult> ListStudentsAsync(ClaimsPrincipal principal, AbderaDbContext db)
    {
        var guardianId = AuthContext.GetUserId(principal);

        var studentIds = await db.StudentGuardians
            .Where(sg => sg.GuardianId == guardianId)
            .Select(sg => sg.StudentId)
            .ToListAsync();

        var students = await db.Students
            .Where(s => studentIds.Contains(s.Id))
            .OrderBy(s => s.FirstName).ThenBy(s => s.LastName)
            .ToListAsync();

        // Bir öğrencinin birden fazla aktif kaydı (enstrümanı) olabilir - başlıkta gösterilecek
        // tek satır için deterministik olarak en eskisini alıyoruz (PrimaryGuardianResolver'daki
        // "birden fazla adayda deterministik ilkini seç" ilkesiyle aynı ruh).
        var primaryEnrollmentByStudent = await db.Enrollments
            .Where(e => studentIds.Contains(e.StudentId) && e.Status == EnrollmentStatus.Active)
            .Join(db.Teachers, e => e.TeacherId, t => t.Id, (e, t) => new { e, t })
            .Join(db.Instruments, x => x.e.InstrumentId, i => i.Id, (x, i) => new { x.e.StudentId, x.e.CreatedAt, TeacherName = x.t.FirstName + " " + x.t.LastName, InstrumentName = i.Name })
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();
        var primaryByStudent = primaryEnrollmentByStudent
            .GroupBy(x => x.StudentId)
            .ToDictionary(g => g.Key, g => g.First());

        return Results.Ok(students.Select(s =>
        {
            primaryByStudent.TryGetValue(s.Id, out var primary);
            return new GuardianStudentResponse(s.Id, s.FirstName, s.LastName, primary?.InstrumentName, primary?.TeacherName);
        }));
    }

    private static async Task<IResult> CalendarAsync(
        Guid studentId, DateTimeOffset from, DateTimeOffset to, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var guardianId = AuthContext.GetUserId(principal);
        await EnsureOwnsStudentAsync(guardianId, studentId, db);

        if (to - from > MaxRange)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["to"] = ["Tarih aralığı en fazla 3 ay olabilir."],
            });
        }

        // Not: OrderBy, record projeksiyonundan ÖNCE uygulanır (CLAUDE.md - EF Core bir record
        // constructor alanına göre sıralamayı SQL'e çeviremiyor, bkz. Calendar.cs).
        var lessons = await db.Lessons
            .Where(l => l.StudentId == studentId && l.StartAt >= from && l.StartAt < to)
            .Join(db.Teachers, l => l.TeacherId, t => t.Id, (l, t) => new { Lesson = l, Teacher = t })
            .Join(db.Instruments, x => x.Lesson.InstrumentId, i => i.Id, (x, i) => new { x.Lesson, x.Teacher, Instrument = i })
            .OrderBy(x => x.Lesson.StartAt)
            .Select(x => new GuardianLessonResponse(
                x.Lesson.Id, x.Lesson.StartAt, x.Lesson.EndAt, x.Lesson.Status,
                x.Instrument.Name, x.Teacher.FirstName + " " + x.Teacher.LastName, RsvpResponse.Unknown))
            .ToListAsync();

        // Admin/Teacher takviminin aksine (herhangi bir velinin cevabını özetler), burada
        // yalnızca ÇAĞIRAN velinin kendi RSVP'si gösterilir - "kendi cevabım ne" sorusu bu.
        var lessonIds = lessons.Select(l => l.Id).ToList();
        var ownRsvpByLesson = await db.LessonRsvps
            .Where(r => lessonIds.Contains(r.LessonId) && r.GuardianId == guardianId)
            .ToDictionaryAsync(r => r.LessonId, r => r.Response);

        return Results.Ok(lessons.Select(lesson => lesson with
        {
            RsvpResponse = ownRsvpByLesson.GetValueOrDefault(lesson.Id, RsvpResponse.Unknown),
        }));
    }

    private static async Task<IResult> SetRsvpAsync(
        Guid lessonId, SetRsvpRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        if (request.Response == RsvpResponse.Unknown)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["response"] = ["Geçerli bir yanıt seç."],
            });
        }

        var guardianId = AuthContext.GetUserId(principal);

        var lesson = await db.Lessons.SingleOrDefaultAsync(l => l.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");

        await EnsureOwnsStudentAsync(guardianId, lesson.StudentId, db);

        if (lesson.Status != LessonStatus.Normal)
        {
            throw new ConflictException($"'{lesson.Status}' durumundaki bir ders için RSVP verilemez.");
        }

        var rsvp = await db.LessonRsvps.SingleOrDefaultAsync(r => r.LessonId == lessonId && r.GuardianId == guardianId);
        if (rsvp is null)
        {
            rsvp = LessonRsvp.Create(lessonId, guardianId, clock.UtcNow);
            db.LessonRsvps.Add(rsvp);
        }

        // docs/05-state-models.md: veli fikir değiştirirse ATTENDING/NOT_ATTENDING arasında
        // serbestçe geçiş yapabilir - burada da (WhatsApp/Admin akışlarıyla aynı) bunu kısıtlamıyoruz;
        // "tekrar değiştirilemesin" isteği yalnızca frontend'de varsayılan kilitli görünüm olarak
        // uygulanıyor, backend invariant'ını bozmuyor.
        rsvp.Respond(request.Response, RsvpSource.GuardianWeb, clock.UtcNow);
        await db.SaveChangesAsync();

        return Results.Ok(new SetRsvpResponse(lessonId, rsvp.Response, rsvp.RespondedAt!.Value));
    }

    private static async Task EnsureOwnsStudentAsync(Guid guardianId, Guid studentId, AbderaDbContext db)
    {
        var owns = await db.StudentGuardians.AnyAsync(sg => sg.GuardianId == guardianId && sg.StudentId == studentId);
        if (!owns)
        {
            throw new ForbiddenException("Bu öğrenci size bağlı değil.");
        }
    }
}
