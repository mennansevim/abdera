using System.Security.Claims;
using Abdera.Api.Modules.Library.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Abdera.Api.Modules.Library.Features;

// Kütüphaneden öğrenciye eser önerisi. Öğretmen yalnızca kendi öğrencisine (onunla bir kurs
// kaydı varsa - Students/Progress'teki sahiplik kuralı) öneri ekler, görür ve kaldırır;
// yönetici hepsine. Öğrenciler arası erişim People'ın iç yapısına değil açık bir Enrollments
// sorgusuna dayanır (CLAUDE.md modüller arası erişim kuralı).
public static class LibrarySuggestions
{
    public record Request(string EntryId, string Title, string? Composer, string? Note);

    public record SuggestionResponse(
        Guid Id, Guid StudentId, string EntryId, string Title, string Composer, string? Note,
        string SuggestedByName, bool CanRemove, DateTimeOffset CreatedAt);

    public static void MapLibrarySuggestions(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/students/{studentId:guid}/library-suggestions", ListAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/students/{studentId:guid}/library-suggestions", CreateAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapDelete("/api/library-suggestions/{id:guid}", DeleteAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> ListAsync(Guid studentId, ClaimsPrincipal user, AbderaDbContext db)
    {
        await EnsureStudentAccessAsync(studentId, user, db);
        var suggestions = await db.LibrarySuggestions.AsNoTracking()
            .Where(s => s.StudentId == studentId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
        return Results.Ok(suggestions.Select(s => ToResponse(s, canRemove: true)));
    }

    private static async Task<IResult> CreateAsync(Guid studentId, Request request, ClaimsPrincipal user, AbderaDbContext db, IClock clock)
    {
        var teacherId = await EnsureStudentAccessAsync(studentId, user, db);

        // Eklenen eser (piece-<id>) gerçekten var olmalı; kitap/Mutopia kataloğu frontend'de durduğu
        // için onların kimliği yalnızca biçim olarak doğrulanır (LibrarySuggestion.Create).
        if (request.EntryId?.StartsWith("piece-", StringComparison.Ordinal) == true)
        {
            var pieceExists = Guid.TryParseExact(request.EntryId["piece-".Length..], "N", out var pieceId)
                && await db.LibraryPieces.AnyAsync(p => p.Id == pieceId);
            if (!pieceExists) throw new NotFoundException("Eser bulunamadı.");
        }

        if (await db.LibrarySuggestions.AnyAsync(s => s.StudentId == studentId && s.EntryId == request.EntryId))
            throw new ConflictException("Bu eser bu öğrenciye zaten önerilmiş.");

        var suggestedBy = teacherId is { } id
            ? await db.Teachers.Where(t => t.Id == id).Select(t => t.FirstName + " " + t.LastName).SingleAsync()
            : "Yönetici";
        var suggestion = LibrarySuggestion.Create(studentId, request.EntryId ?? string.Empty, request.Title,
            request.Composer ?? string.Empty, request.Note, teacherId, suggestedBy, clock.UtcNow);
        db.LibrarySuggestions.Add(suggestion);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı anda iki öneri: UNIQUE (student_id, entry_id) ikincisini durdurur.
            throw new ConflictException("Bu eser bu öğrenciye zaten önerilmiş.");
        }

        return Results.Created($"/api/library-suggestions/{suggestion.Id}", ToResponse(suggestion, canRemove: true));
    }

    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal user, AbderaDbContext db)
    {
        var suggestion = await db.LibrarySuggestions.SingleOrDefaultAsync(s => s.Id == id)
            ?? throw new NotFoundException("Öneri bulunamadı.");
        await EnsureStudentAccessAsync(suggestion.StudentId, user, db);
        db.LibrarySuggestions.Remove(suggestion);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // Yöneticide null, öğretmende kendi Teacher.Id'si döner.
    private static async Task<Guid?> EnsureStudentAccessAsync(Guid studentId, ClaimsPrincipal user, AbderaDbContext db)
    {
        if (!await db.Students.AnyAsync(s => s.Id == studentId))
            throw new NotFoundException("Öğrenci bulunamadı.");
        var teacherId = await AuthContext.ResolveTeacherScopeAsync(user, db);
        if (teacherId is { } scoped && !await db.Enrollments.AnyAsync(e => e.StudentId == studentId && e.TeacherId == scoped))
            throw new ForbiddenException("Bu öğrenci size atanmamış.");
        return teacherId;
    }

    private static SuggestionResponse ToResponse(LibrarySuggestion s, bool canRemove) =>
        new(s.Id, s.StudentId, s.EntryId, s.Title, s.Composer, s.Note, s.SuggestedByName, canRemove, s.CreatedAt);
}
