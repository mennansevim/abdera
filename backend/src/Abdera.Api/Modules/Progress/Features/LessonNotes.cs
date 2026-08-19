using System.Security.Claims;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Progress.Features;

// docs/07-api.md POST /api/lessons/{lessonId}/notes. docs/04-permissions.md: "Ders notu ...
// girme - Admin salt okuma, Teacher yalnızca kendi öğrencisi." Not silinmez/güncellenmez -
// her giriş yeni bir satır (bir derse birden fazla not eklenebilir, ERD'de UNIQUE yok).
public static class LessonNotes
{
    public record CreateRequest(string? Practiced, string? Note, string? Homework, string? NextGoal);
    public record LessonNoteResponse(Guid Id, Guid LessonId, Guid TeacherId, string? Practiced, string? Note, string? Homework, string? NextGoal, DateTimeOffset CreatedAt);

    public static void MapLessonNotes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lessons/{lessonId:guid}/notes", ListAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/lessons/{lessonId:guid}/notes", CreateAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> ListAsync(Guid lessonId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(l => l.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");
        await EnsureTeacherOwnsLessonAsync(lesson.TeacherId, principal, db);

        var notes = await db.LessonNotes.Where(n => n.LessonId == lessonId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new LessonNoteResponse(n.Id, n.LessonId, n.TeacherId, n.Practiced, n.Note, n.Homework, n.NextGoal, n.CreatedAt))
            .ToListAsync();

        return Results.Ok(notes);
    }

    private static async Task<IResult> CreateAsync(Guid lessonId, CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(l => l.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");

        // docs/04-permissions.md: Admin bu uç noktada salt okuma - not girme yalnızca Teacher.
        if (AuthContext.IsAdmin(principal))
            throw new ForbiddenException("Ders notu yalnızca öğretmen tarafından girilebilir.");

        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is { } teacherId && teacherId != lesson.TeacherId)
            throw new ForbiddenException("Bu ders size atanmamış.");

        var note = LessonNote.Create(lessonId, lesson.TeacherId, request.Practiced, request.Note, request.Homework, request.NextGoal, clock.UtcNow);
        db.LessonNotes.Add(note);
        await db.SaveChangesAsync();

        return Results.Created($"/api/lessons/{lessonId}/notes/{note.Id}",
            new LessonNoteResponse(note.Id, note.LessonId, note.TeacherId, note.Practiced, note.Note, note.Homework, note.NextGoal, note.CreatedAt));
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
