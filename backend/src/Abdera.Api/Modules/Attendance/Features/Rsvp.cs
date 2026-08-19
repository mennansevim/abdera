using System.Security.Claims;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Attendance.Features;

// docs/07-api.md'de ayrı bir RSVP uç noktası listelenmemişti - WhatsApp (Phase 5) gelene kadar
// velinin "geliyorum/gelemiyorum" cevabını yönetici sözlü/telefonla alıp elle girebilmesi
// gerekiyor (source=ADMIN). docs/04-permissions.md: "RSVP durumu görüntüleme - Admin tümü,
// Teacher yalnızca kendi dersleri" - ayarlamak ise şimdilik yalnızca Admin (WhatsApp akışının
// yerini tutan geçici kanal).
public static class Rsvp
{
    public record SetRequest(Guid GuardianId, RsvpResponse Response);
    public record RsvpItem(Guid Id, Guid GuardianId, string GuardianName, RsvpResponse Response, DateTimeOffset? RespondedAt, RsvpSource Source);

    public static void MapRsvp(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lessons/{lessonId:guid}/rsvp", ListAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/lessons/{lessonId:guid}/rsvp", SetAsync).RequireAuthorization(AuthorizationPolicies.AdminOnly);
    }

    private static async Task<IResult> ListAsync(Guid lessonId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(l => l.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");

        await EnsureTeacherOwnsLessonAsync(lesson.TeacherId, principal, db);

        var items = await db.LessonRsvps.Where(r => r.LessonId == lessonId)
            .Join(db.Guardians, r => r.GuardianId, g => g.Id, (r, g) =>
                new RsvpItem(r.Id, g.Id, g.FirstName + " " + g.LastName, r.Response, r.RespondedAt, r.Source))
            .ToListAsync();

        return Results.Ok(items);
    }

    private static async Task<IResult> SetAsync(Guid lessonId, SetRequest request, AbderaDbContext db, IClock clock)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(l => l.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");

        var guardianLinked = await db.StudentGuardians
            .AnyAsync(sg => sg.StudentId == lesson.StudentId && sg.GuardianId == request.GuardianId);
        if (!guardianLinked)
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["guardianId"] = ["Bu veli, dersin öğrencisiyle ilişkilendirilmemiş."],
            });

        var rsvp = await db.LessonRsvps.SingleOrDefaultAsync(r => r.LessonId == lessonId && r.GuardianId == request.GuardianId);
        if (rsvp is null)
        {
            rsvp = LessonRsvp.Create(lessonId, request.GuardianId, clock.UtcNow);
            db.LessonRsvps.Add(rsvp);
        }

        rsvp.Respond(request.Response, RsvpSource.Admin, clock.UtcNow);
        await db.SaveChangesAsync();

        var guardian = await db.Guardians.SingleAsync(g => g.Id == request.GuardianId);
        return Results.Ok(new RsvpItem(rsvp.Id, guardian.Id, $"{guardian.FirstName} {guardian.LastName}", rsvp.Response, rsvp.RespondedAt, rsvp.Source));
    }

    private static async Task EnsureTeacherOwnsLessonAsync(Guid lessonTeacherId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is { } teacherId && teacherId != lessonTeacherId)
        {
            throw new ForbiddenException("Bu ders size atanmamış.");
        }
    }
}
