using System.Security.Claims;
using System.Text.Json;
using Abdera.Api.Modules.Attendance.Features;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Modules.Progress.Infrastructure;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Progress.Features;

// docs/07-api.md POST /api/lessons/{lessonId}/notes. docs/04-permissions.md: "Ders notu ...
// girme - Admin salt okuma, Teacher yalnızca kendi öğrencisi." Not güncellenmez - her giriş
// yeni bir satır (bir derse birden fazla not eklenebilir, ERD'de UNIQUE yok). Yanlış yazılan
// notu öğretmen kendisi silebilir (kullanıcı isteği: "öğretmenler notları silebilsin"); not
// finansal kayıt değil, silme kalıcıdır ve audit_log'a yazılır.
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

    // Not formundaki "Ders devam ediyor" kısayolu için: aynı öğrencinin aynı enstrümandaki bir
    // önceki dersinden taşınabilecek alanlar. Eser alanları en son eser yazılmış nottan gelir
    // (her not eser içermez; araya eser yazılmamış bir not girdi diye eser kaybolmamalı),
    // ödev/hedef/çalışılan ise en son nottan. Kullanıcı isteği: "bir önceki şarkıya devam
    // ettiklerinde bunu seçemiyorlar".
    public record PreviousNoteResponse(
        DateTimeOffset LessonStartAt,
        string? Practiced,
        string? Homework,
        string? NextGoal,
        string? PieceTitle,
        string? PieceComposer,
        int? PieceDifficulty,
        RepertoireStatus? PieceStatus);

    public record PreviousResponse(PreviousNoteResponse? Previous);

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
        app.MapGet("/api/lessons/{lessonId:guid}/notes/previous", PreviousAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPut("/api/lesson-notes/{noteId:guid}/parent-comment", SetParentCommentAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapPost("/api/lesson-notes/{noteId:guid}/parent-comment/revoke", RevokeParentCommentAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
        app.MapDelete("/api/lesson-notes/{noteId:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
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

    private static async Task<IResult> PreviousAsync(Guid lessonId, ClaimsPrincipal principal, AbderaDbContext db)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(l => l.Id == lessonId)
            ?? throw new NotFoundException("Ders bulunamadı.");
        await EnsureTeacherOwnsLessonAsync(lesson.TeacherId, principal, db);

        // "Önceki" bu dersten önce BAŞLAYAN dersler: eski bir derse not sonradan yazılırken
        // ondan sonraki derslerin notu öneri olarak gelmesin. Öğretmen değişmiş olabilir -
        // aynı öğrenci + enstrüman yeterli.
        var earlierNotes = db.LessonNotes
            .Join(db.Lessons, note => note.LessonId, earlier => earlier.Id,
                (note, earlier) => new { Note = note, Lesson = earlier })
            .Where(x => x.Lesson.StudentId == lesson.StudentId
                && x.Lesson.InstrumentId == lesson.InstrumentId
                && x.Lesson.StartAt < lesson.StartAt)
            .OrderByDescending(x => x.Lesson.StartAt)
            .ThenByDescending(x => x.Note.CreatedAt);

        var latest = await earlierNotes.FirstOrDefaultAsync();
        if (latest is null) return Results.Ok(new PreviousResponse(null));

        // Eser yalnızca EN SON nottan gelir. Not formlarında eser alanı artık yok (kullanıcı
        // isteği: "çalışılan eser kısmını not girişinden kaldır"); geçmişte eser girilmiş en son
        // notu aramak, öğrenci çoktan başka şeye geçmişken haftalar önceki eseri "devam" diye
        // önermeye devam ederdi. Arşive kaldırılmış (bitmiş) eser de önerilmez.
        var latestPiece = latest.Note.PieceTitle != null && latest.Note.PieceStatus != RepertoireStatus.Archived
            ? latest.Note
            : null;

        return Results.Ok(new PreviousResponse(new PreviousNoteResponse(
            latest.Lesson.StartAt,
            latest.Note.Practiced,
            latest.Note.Homework,
            latest.Note.NextGoal,
            latestPiece?.PieceTitle,
            latestPiece?.PieceComposer,
            latestPiece?.PieceDifficulty,
            latestPiece?.PieceStatus)));
    }

    private static async Task<IResult> CreateAsync(
        Guid lessonId, CreateRequest request, ClaimsPrincipal principal, AbderaDbContext db, IClock clock, IStaffNotifier notifier)
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
        // Not yazılan derse öğrenci gelmiştir: yoklama hiç girilmediyse "geldi" olarak aynı
        // kayıtla işlenir ve zildeki "öğrenci geldi mi?" sorusu kapanır.
        await AttendanceFromNote.MarkPresentIfUnmarkedAsync(db, clock, notifier, lesson, AuthContext.GetUserId(principal));
        await db.SaveChangesAsync();

        // Yorum bekleyen son ders de yazıldıysa zildeki hatırlatma hemen kapanır (not önce
        // kaydedilmeli ki "bekleyen ders kaldı mı" sorgusu onu görsün).
        if (await LessonNoteReminderJob.ClearIfDoneAsync(db, clock, notifier, lesson.TeacherId))
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

    private static async Task<IResult> DeleteAsync(
        Guid noteId,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock)
    {
        // Not girme gibi silme de yalnızca notu yazan öğretmenin işi; Admin salt okuma.
        if (AuthContext.IsAdmin(principal))
            throw new ForbiddenException("Ders notunu yalnızca yazan öğretmen silebilir.");
        var note = await db.LessonNotes.SingleOrDefaultAsync(item => item.Id == noteId)
            ?? throw new NotFoundException("Ders notu bulunamadı.");
        var teacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db)
            ?? throw new ForbiddenException("Öğretmen kaydı bulunamadı.");
        if (teacherId != note.TeacherId)
            throw new ForbiddenException("Bu ders notu size ait değil.");

        var studentId = await db.Lessons
            .Where(lesson => lesson.Id == note.LessonId)
            .Select(lesson => lesson.StudentId)
            .SingleAsync();

        db.LessonNotes.Remove(note);
        // Not içeriği audit'e kopyalanmaz (oluşturmadaki gibi yalnızca bayraklar) - silinen
        // metin başka bir tabloda yaşamaya devam etmesin.
        db.AuditLogs.Add(AuditLog.Record(
            AuthContext.GetUserId(principal),
            "lesson_note.deleted",
            nameof(LessonNote),
            note.Id,
            clock.UtcNow,
            beforeJson: JsonSerializer.Serialize(new
            {
                note.LessonId,
                note.TeacherId,
                note.CreatedAt,
                HasRawNote = note.Note is not null,
                HasPiece = note.PieceTitle is not null,
                HasParentComment = note.ParentComment is not null,
                note.ParentCommentApprovedAt,
            })));
        // Kayıtlı "Genel gelişim" yorumu silinen notu da özetliyor olabilir; aylık yenileme
        // politikası onu ay sonuna kadar göstermeye devam ederdi. Öğrencinin tüm kapsamlardaki
        // önbelleği düşer, bir sonraki açılışta kalan notlardan yeniden üretilir.
        db.ProgressSummaries.RemoveRange(
            await db.ProgressSummaries.Where(summary => summary.StudentId == studentId).ToListAsync());
        await db.SaveChangesAsync();
        return Results.NoContent();
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
