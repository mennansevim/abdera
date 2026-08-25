using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Progress.Features;

// docs/07-api.md POST /api/lessons/{lessonId}/notes. docs/04-permissions.md: "Ders notu ...
// girme - Admin salt okuma, Teacher yalnızca kendi öğrencisi." Not silinmez/güncellenmez -
// her giriş yeni bir satır (bir derse birden fazla not eklenebilir, ERD'de UNIQUE yok).
public static class LessonNotes
{
    public record CreateRequest(
        string? Practiced,
        string? Note,
        string? Homework,
        string? NextGoal,
        string? PieceTitle = null,
        int? PieceDifficulty = null,
        string? PieceComposer = null,
        RepertoireStatus? PieceStatus = null,
        DateOnly? PieceTargetDate = null,
        string? PieceResourceUrl = null,
        bool PieceResourceVisibleToGuardian = false);

    public record ParentCommentRequest(string ParentComment, bool Approve);

    public record SuggestParentCommentResponse(string Suggestion);

    public record LessonNoteResponse(
        Guid Id,
        Guid LessonId,
        Guid TeacherId,
        string? Practiced,
        string? Note,
        string? Homework,
        string? NextGoal,
        string? PieceTitle,
        int? PieceDifficulty,
        string? PieceComposer,
        RepertoireStatus? PieceStatus,
        DateOnly? PieceTargetDate,
        string? PieceResourceUrl,
        bool PieceResourceVisibleToGuardian,
        string? ParentComment,
        DateTimeOffset? ParentCommentApprovedAt,
        Guid? ParentCommentApprovedBy,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public static void MapLessonNotes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lessons/{lessonId:guid}/notes", ListAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/lessons/{lessonId:guid}/notes", CreateAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPut("/api/lesson-notes/{noteId:guid}/parent-comment", SetParentCommentAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/lesson-notes/{noteId:guid}/parent-comment/revoke", RevokeParentCommentAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/lesson-notes/{noteId:guid}/parent-comment/suggest", SuggestParentCommentAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    // Faz 10: ham notu veliye uygun yapıcı bir metne çevirir ve YALNIZCA öneri döndürür.
    // Hiçbir şey kaydedilmez ve onaylanmaz - öğretmen öneriyi düzenleyip
    // PUT .../parent-comment ile kaydeder, ya da tamamen yok sayar.
    private static async Task<IResult> SuggestParentCommentAsync(
        Guid noteId,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock,
        IConstructiveTextRewriter rewriter)
    {
        // Aynı yetki sınırı SetParentCommentAsync ile birebir: veli yorumu öğretmenin işi.
        if (AuthContext.IsAdmin(principal))
            throw new ForbiddenException("Veli yorumunu yalnızca öğretmen düzenleyip onaylayabilir.");

        var note = await db.LessonNotes.SingleOrDefaultAsync(item => item.Id == noteId)
            ?? throw new NotFoundException("Ders notu bulunamadı.");
        var teacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db)
            ?? throw new ForbiddenException("Öğretmen kaydı bulunamadı.");
        if (teacherId != note.TeacherId)
            throw new ForbiddenException("Bu ders notu size ait değil.");

        if (!rewriter.IsAvailable)
            throw new ConflictException("Yapıcı metne dönüştürme kapalı: okul için bir AI sağlayıcısı yapılandırılmamış.");

        if (string.IsNullOrWhiteSpace(note.Note))
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["note"] = ["Dönüştürülecek bir ders notu yok."],
            });

        var studentFirstName = await db.Lessons
            .Where(lesson => lesson.Id == note.LessonId)
            .Join(db.Students, lesson => lesson.StudentId, student => student.Id, (_, student) => student.FirstName)
            .SingleOrDefaultAsync();

        var result = await rewriter.RewriteAsync(
            new ConstructiveRewriteRequest(note.Note, studentFirstName, note.PieceTitle));

        // Sağlayıcı hatası kullanıcıya anlaşılır bir 409 olarak döner (500/stack trace değil);
        // öğretmen yorumu elle yazmaya devam edebilir.
        if (!result.Success || string.IsNullOrWhiteSpace(result.Suggestion))
            throw new ConflictException(result.Error ?? "Yapıcı metin önerisi üretilemedi.");

        // Audit: isteğin yapıldığı kaydedilir, ham not veya öneri metni YAZILMAZ
        // (CreateAsync'teki HasRawNote deseninin aynısı - audit hassas içeriği çoğaltmaz).
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "lesson_note.ai_suggestion_requested",
            nameof(LessonNote),
            note.Id,
            clock.UtcNow,
            afterJson: JsonSerializer.Serialize(new { note.TeacherId, SuggestionLength = result.Suggestion.Length })));
        await db.SaveChangesAsync();

        return Results.Ok(new SuggestParentCommentResponse(result.Suggestion));
    }

    private static async Task<IResult> ListAsync(Guid lessonId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(l => l.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");
        await EnsureTeacherOwnsLessonAsync(lesson.TeacherId, principal, db);

        var noteRows = await db.LessonNotes.Where(n => n.LessonId == lessonId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();

        return Results.Ok(noteRows.Select(ToResponse));
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

        if (request.PieceDifficulty is < 1 or > 5)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                [nameof(request.PieceDifficulty)] = ["Eser zorluğu 1 ile 5 arasında olmalı."],
            });
        }
        if (!string.IsNullOrWhiteSpace(request.PieceResourceUrl) &&
            (!Uri.TryCreate(request.PieceResourceUrl, UriKind.Absolute, out var resourceUri) ||
             resourceUri.Scheme is not ("https" or "http")))
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                [nameof(request.PieceResourceUrl)] = ["Eser bağlantısı geçerli bir http/https adresi olmalı."],
            });
        }

        var note = LessonNote.Create(
            lessonId,
            lesson.TeacherId,
            request.Practiced,
            request.Note,
            request.Homework,
            request.NextGoal,
            request.PieceTitle,
            request.PieceDifficulty,
            clock.UtcNow,
            request.PieceComposer,
            request.PieceStatus,
            request.PieceTargetDate,
            request.PieceResourceUrl,
            request.PieceResourceVisibleToGuardian);
        db.LessonNotes.Add(note);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "lesson_note.created",
            nameof(LessonNote),
            note.Id,
            clock.UtcNow,
            afterJson: JsonSerializer.Serialize(new
            {
                note.LessonId,
                note.TeacherId,
                HasRawNote = note.Note is not null,
                HasPiece = note.PieceTitle is not null,
                note.PieceDifficulty,
            })));
        await db.SaveChangesAsync();

        return Results.Created($"/api/lessons/{lessonId}/notes/{note.Id}",
            ToResponse(note));
    }

    private static async Task<IResult> SetParentCommentAsync(
        Guid noteId,
        ParentCommentRequest request,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock)
    {
        if (AuthContext.IsAdmin(principal))
            throw new ForbiddenException("Veli yorumunu yalnızca öğretmen düzenleyip onaylayabilir.");

        var note = await db.LessonNotes.SingleOrDefaultAsync(item => item.Id == noteId)
            ?? throw new NotFoundException("Ders notu bulunamadı.");
        var teacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db)
            ?? throw new ForbiddenException("Öğretmen kaydı bulunamadı.");
        if (teacherId != note.TeacherId)
            throw new ForbiddenException("Bu ders notu size ait değil.");
        if (string.IsNullOrWhiteSpace(request.ParentComment))
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["parentComment"] = ["Veli yorumu boş olamaz."],
            });

        var before = JsonSerializer.Serialize(new
        {
            HasParentComment = note.ParentComment is not null,
            note.ParentCommentApprovedAt,
        });
        note.SetParentCommentDraft(request.ParentComment, clock.UtcNow);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "lesson_note.parent_comment_edited",
            nameof(LessonNote),
            note.Id,
            clock.UtcNow,
            before,
            JsonSerializer.Serialize(new { HasParentComment = true, Approved = false })));

        if (request.Approve)
        {
            note.ApproveParentComment(teacherId, clock.UtcNow);
            db.AuditLogs.Add(AuditLog.Record(
                AuthContext.GetUserId(principal),
                "lesson_note.parent_comment_approved",
                nameof(LessonNote),
                note.Id,
                clock.UtcNow,
                afterJson: JsonSerializer.Serialize(new { note.ParentCommentApprovedAt, note.ParentCommentApprovedBy })));
        }

        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(note));
    }

    private static async Task<IResult> RevokeParentCommentAsync(
        Guid noteId,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock)
    {
        if (AuthContext.IsAdmin(principal))
            throw new ForbiddenException("Veli yorumunu yalnızca öğretmen geri çekebilir.");
        var note = await db.LessonNotes.SingleOrDefaultAsync(item => item.Id == noteId)
            ?? throw new NotFoundException("Ders notu bulunamadı.");
        var teacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db)
            ?? throw new ForbiddenException("Öğretmen kaydı bulunamadı.");
        if (teacherId != note.TeacherId)
            throw new ForbiddenException("Bu ders notu size ait değil.");

        note.RevokeParentComment(teacherId, clock.UtcNow);
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "lesson_note.parent_comment_revoked",
            nameof(LessonNote),
            note.Id,
            clock.UtcNow));
        await db.SaveChangesAsync();
        return Results.Ok(ToResponse(note));
    }

    private static async Task EnsureTeacherOwnsLessonAsync(Guid lessonTeacherId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var teacherScope = await AuthContext.ResolveTeacherScopeAsync(principal, db);
        if (teacherScope is { } teacherId && teacherId != lessonTeacherId)
        {
            throw new ForbiddenException("Bu ders size atanmamış.");
        }
    }

    private static LessonNoteResponse ToResponse(LessonNote note) => new(
        note.Id,
        note.LessonId,
        note.TeacherId,
        note.Practiced,
        note.Note,
        note.Homework,
        note.NextGoal,
        note.PieceTitle,
        note.PieceDifficulty,
        note.PieceComposer,
        note.PieceStatus,
        note.PieceTargetDate,
        note.PieceResourceUrl,
        note.PieceResourceVisibleToGuardian,
        note.ParentComment,
        note.ParentCommentApprovedAt,
        note.ParentCommentApprovedBy,
        note.CreatedAt,
        note.UpdatedAt);
}
