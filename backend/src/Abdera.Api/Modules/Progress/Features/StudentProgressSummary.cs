using System.Security.Claims;
using Abdera.Api.Modules.Progress.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Abdera.Api.Modules.Progress.Features;

// Gelişim ekranındaki "Genel gelişim" yorumu: öğretmen notlarından AI ile üretilen kısa özet.
//
// Üretim tembeldir (lazy): ekran açıldığında önbellekteki yorum kaynak notlarla hâlâ
// uyuşuyorsa aynen döner; yeni bir not girilmişse sağlayıcıya bir kez gidilir ve önbellek
// yenilenir. Böylece "gelişim girdikçe yorum güncellenir" sağlanırken not kaydetme akışı
// sağlayıcının gecikmesine veya hatasına hiç bağlı kalmaz.
public static class StudentProgressSummary
{
    public enum SummaryStatus
    {
        Ready,      // Güncel yorum (ya da sağlayıcı geçici hata verdiyse son üretilen, IsStale=true)
        NoNotes,    // Yorumlanacak not yok
        Unavailable, // AI sağlayıcısı yapılandırılmamış ve daha önce üretilmiş yorum yok
        Failed,     // Sağlayıcı hata verdi ve gösterilecek önceki yorum yok
    }

    public record Response(
        SummaryStatus Status,
        string? Summary,
        DateTimeOffset? GeneratedAt,
        int SourceNoteCount,
        bool IsStale);

    public static void MapStudentProgressSummary(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/students/{studentId:guid}/progress-summary", GetAsync)
            .RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    private static async Task<IResult> GetAsync(
        Guid studentId,
        ClaimsPrincipal principal,
        AbderaDbContext db,
        IClock clock,
        IProgressSummaryGenerator generator,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // StudentProgress.ListAsync ile aynı kapsam: öğretmen yalnızca kendi öğrencisini ve
        // yalnızca KENDİ notlarını görür; yorum da yalnızca o notlardan üretilir.
        var teacherScope = await ProgressAuthorization.EnsureStudentAccessAsync(studentId, principal, db);

        var notes =
            from note in db.LessonNotes
            join lesson in db.Lessons on note.LessonId equals lesson.Id
            where lesson.StudentId == studentId
            select new { note, lesson };
        if (teacherScope is { } scopedTeacherId)
            notes = notes.Where(item => item.note.TeacherId == scopedTeacherId);

        var noteCount = await notes.CountAsync(cancellationToken);
        if (noteCount == 0)
            return Results.Ok(new Response(SummaryStatus.NoNotes, null, null, 0, false));
        var latestNoteAt = await notes.MaxAsync(item => item.note.CreatedAt, cancellationToken);

        var cached = await db.ProgressSummaries.SingleOrDefaultAsync(
            summary => summary.StudentId == studentId && summary.TeacherId == teacherScope,
            cancellationToken);
        if (cached is not null && cached.IsCurrentFor(noteCount, latestNoteAt))
            return Results.Ok(ToResponse(cached, isStale: false));

        if (!generator.IsAvailable)
        {
            return Results.Ok(cached is not null
                ? ToResponse(cached, isStale: true)
                : new Response(SummaryStatus.Unavailable, null, null, noteCount, false));
        }

        // Sıralama projeksiyondan ÖNCE, ham ara tip üzerinde (CLAUDE.md "OrderBy sırası").
        var recent = await (
                from item in notes
                join instrument in db.Instruments on item.lesson.InstrumentId equals instrument.Id
                select new { item.note, item.lesson.StartAt, InstrumentName = instrument.Name })
            .OrderByDescending(item => item.StartAt)
            .ThenByDescending(item => item.note.CreatedAt)
            .Take(Infrastructure.OpenAiProgressSummaryGenerator.MaxNotes)
            .ToListAsync(cancellationToken);
        var studentFirstName = await db.Students
            .Where(student => student.Id == studentId)
            .Select(student => student.FirstName)
            .SingleAsync(cancellationToken);

        var request = new ProgressSummaryRequest(
            studentFirstName,
            recent.AsEnumerable().Reverse().Select(item => new ProgressSummaryNote(
                item.StartAt,
                item.InstrumentName,
                item.note.PieceTitle,
                item.note.PieceDifficulty,
                item.note.Practiced,
                item.note.Note,
                item.note.Homework,
                item.note.NextGoal)).ToList());

        var result = await generator.GenerateAsync(request, cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Summary))
        {
            // Sağlayıcı hatası ekranı bozmaz: varsa son yorum "güncel değil" işaretiyle döner.
            loggerFactory.CreateLogger(typeof(StudentProgressSummary))
                .LogWarning("Gelişim yorumu üretilemedi: {Error}", result.Error);
            return Results.Ok(cached is not null
                ? ToResponse(cached, isStale: true)
                : new Response(SummaryStatus.Failed, null, null, noteCount, false));
        }

        var summaryText = result.Summary.Length > 2000 ? result.Summary[..2000] : result.Summary;
        var now = clock.UtcNow;
        if (cached is null)
        {
            cached = ProgressSummary.Create(studentId, teacherScope, summaryText, noteCount, latestNoteAt, generator.ModelName, now);
            db.ProgressSummaries.Add(cached);
        }
        else
        {
            cached.Refresh(summaryText, noteCount, latestNoteAt, generator.ModelName, now);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı öğrenci için iki ekran aynı anda ilk yorumu üretti; diğerinin yazdığı satır
            // geçerli. Bu isteğin ürettiği metin yine de gösterilebilir - ikisi de aynı notlardan.
        }

        return Results.Ok(new Response(SummaryStatus.Ready, summaryText, now, noteCount, false));
    }

    private static Response ToResponse(ProgressSummary summary, bool isStale) =>
        new(SummaryStatus.Ready, summary.Summary, summary.UpdatedAt, summary.SourceNoteCount, isStale);
}
