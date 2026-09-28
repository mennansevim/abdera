using System.Security.Claims;
using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Progress.Features;

// Kullanıcı isteği: "öğretmenlere tamamlanan dersler ile ilgili yorum girmelerini
// hatırlatmalara ekleyelim eğer girmedilerse." Hatırlatmanın hem ekranda (öğretmen ana
// ekranındaki "Yorum bekleyen dersler" kartı) hem zilde (LessonNoteReminderJob ->
// StaffNotificationType.LessonNoteMissing) aynı listeyi göstermesi için "yorum eksik"
// tanımı TEK yerde durur: ListForTeacherAsync.
//
// Tanım: yoklaması GELDİ olarak girilmiş (dolayısıyla COMPLETED) ve bitmiş bir dersin hiç
// ders notu yoksa o ders yorum bekliyordur. Gelmeyen/mazeretli dersten yorum beklenmez -
// öğretmeni yazacak bir şeyin olmadığı ders için dürtmek hatırlatmayı gürültüye çevirir.
public static class PendingLessonNotes
{
    // Geriye dönük pencere: bundan eskisi için hatırlatma üretmek anlamsız (öğretmen o dersi
    // hatırlamıyor) ve liste birikmiş geçmişle dolar. Gelişim ekranındaki not geçmişi elbette
    // tam kalır - burada yalnızca "hâlâ yazılabilir" sayılan pencere daraltılıyor.
    public const int LookbackDays = 14;

    // 14 günlük pencere × 6-8 öğretmenlik bir okulda pratikte onlarca satır; yine de yanıtın
    // sınırsız büyümemesi için tavan (docs/13-audit-fix-prompt.md ARC-3 ile aynı gerekçe).
    private const int MaxItems = 60;

    public record PendingLessonNoteResponse(
        Guid LessonId,
        DateTimeOffset StartAt,
        DateTimeOffset EndAt,
        Guid StudentId,
        string StudentName,
        Guid InstrumentId,
        string InstrumentName);

    public record ListResponse(List<PendingLessonNoteResponse> Items, int LookbackDays);

    public static void MapPendingLessonNotes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/me/pending-lesson-notes", ListAsync).RequireAuthorization(AuthorizationPolicies.TeacherOrAdmin);
    }

    // /api/me/notifications ile aynı kalıp: hedef her zaman oturumun kendisi, URL'de id yok.
    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, AbderaDbContext db, IClock clock)
    {
        // Not girme zaten yalnızca öğretmene ait (docs/04-permissions.md, LessonNotes.cs) -
        // bu liste de öğretmenin kendi yapılacak listesidir, yöneticide karşılığı yok.
        var teacherId = await AuthContext.ResolveTeacherScopeAsync(principal, db)
            ?? throw new ForbiddenException("Yorum bekleyen ders listesi öğretmenin kendi çalışma listesidir.");

        var items = await ListForTeacherAsync(db, clock, teacherId);
        return Results.Ok(new ListResponse(items, LookbackDays));
    }

    public static async Task<List<PendingLessonNoteResponse>> ListForTeacherAsync(
        AbderaDbContext db, IClock clock, Guid teacherId, CancellationToken cancellationToken = default)
    {
        // Not: OrderBy, record'a projeksiyondan ÖNCE - EF Core record constructor alanına göre
        // sıralamayı SQL'e çeviremiyor (CLAUDE.md "Çok tablolu sorgularda OrderBy sırası").
        // Sıralama en eskiden yeniye: en çok unutulan (en eski) ders listenin başında durur.
        return await PendingLessons(db, clock, teacherId)
            .Join(db.Students, lesson => lesson.StudentId, student => student.Id,
                (lesson, student) => new { Lesson = lesson, Student = student })
            .Join(db.Instruments, x => x.Lesson.InstrumentId, instrument => instrument.Id,
                (x, instrument) => new { x.Lesson, x.Student, Instrument = instrument })
            .OrderBy(x => x.Lesson.StartAt)
            .Take(MaxItems)
            .Select(x => new PendingLessonNoteResponse(
                x.Lesson.Id,
                x.Lesson.StartAt,
                x.Lesson.EndAt,
                x.Student.Id,
                x.Student.FirstName + " " + x.Student.LastName,
                x.Instrument.Id,
                x.Instrument.Name))
            .ToListAsync(cancellationToken);
    }

    public static Task<bool> AnyForTeacherAsync(
        AbderaDbContext db, IClock clock, Guid teacherId, CancellationToken cancellationToken = default) =>
        PendingLessons(db, clock, teacherId).AnyAsync(cancellationToken);

    private static IQueryable<Lesson> PendingLessons(AbderaDbContext db, IClock clock, Guid teacherId)
    {
        var now = clock.UtcNow;
        var since = now.AddDays(-LookbackDays);

        return db.Lessons
            .Where(lesson => lesson.TeacherId == teacherId)
            .Where(lesson => lesson.Status == LessonStatus.Completed)
            .Where(lesson => lesson.EndAt <= now && lesson.EndAt >= since)
            .Where(lesson => db.LessonAttendances.Any(attendance =>
                attendance.LessonId == lesson.Id && attendance.Status == AttendanceStatus.Present))
            .Where(lesson => !db.LessonNotes.Any(note => note.LessonId == lesson.Id && (
                note.Note != null || note.Practiced != null || note.Homework != null ||
                note.NextGoal != null || note.PieceTitle != null)));
    }
}
