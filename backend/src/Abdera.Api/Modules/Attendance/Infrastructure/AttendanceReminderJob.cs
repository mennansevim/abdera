using System.Globalization;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Abdera.Api.Modules.Attendance.Infrastructure;

// Kullanıcı isteği: "öğretmenlere bildirim olarak öğrencinin derse katılım sağlayıp
// sağlamadıklarını sor ... seçim yapılmayanlar bildirimlerde kalsın ... işaretleyene kadar."
//
// Bitmiş ama yoklaması girilmemiş her ders için dersin öğretmenine BİR soru düşer
// (StaffNotificationType.AttendanceMissing, referans = ders). LessonNoteMissing'in tersine
// ders başına ayrı satırdır, çünkü her biri zilde kendi Geldi/Gelmedi düğmeleriyle cevaplanır.
// Soru okundu işaretlenerek kapanmaz (StaffNotifications bunu reddeder); yalnızca yoklama
// girilince (MarkAttendance, ders notundan otomatik "geldi" - AttendanceFromNote) ya da ders
// artık yoklama beklemiyorsa (iptal edildi) bu turda kapanır.
//
// Yalnızca ekran içi bildirim - e-posta yok: ders başına bir e-posta günde birkaç tane olurdu.
public static class AttendanceReminderJob
{
    public const string ReferenceType = "lesson";

    // Soru açılan geriye dönük pencere. Kısa tutuldu: özellik ilk açıldığında (ya da bir
    // öğretmen haftalarca yoklama girmediyse) zil geçmiş derslerin sorularıyla dolmasın.
    // Pencereden çıkan AÇIK soru kapanmaz - kullanıcı isteği "işaretleyene kadar kalsın".
    public const int LookbackDays = 7;

    public const string Title = "Öğrenci derse geldi mi?";

    public record Result(int AskedCount, int ClearedCount);

    public static async Task<Result> RunAsync(
        AbderaDbContext db, IClock clock, IStaffNotifier notifier, CancellationToken cancellationToken = default)
    {
        try
        {
            return await RunOnceAsync(db, clock, notifier, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı anda iki tur aynı dersin sorusunu açmaya çalıştıysa UNIQUE (user_id, type,
            // reference_type, reference_id) ikincisini reddeder; yeniden kurulan turda soru
            // artık var ve NotifyTeacherAsync onu atlar.
            db.ChangeTracker.Clear();
            return await RunOnceAsync(db, clock, notifier, cancellationToken);
        }
    }

    private static async Task<Result> RunOnceAsync(
        AbderaDbContext db, IClock clock, IStaffNotifier notifier, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var since = now.AddDays(-LookbackDays);

        // Not: OrderBy, projeksiyondan ÖNCE (CLAUDE.md "Çok tablolu sorgularda OrderBy sırası").
        // Giriş hesabı olmayan öğretmenin zili yok; NotifyTeacherAsync da satır açmazdı.
        var pending = await AwaitingAttendance(db)
            .Where(lesson => lesson.EndAt <= now && lesson.EndAt >= since)
            .Join(db.Teachers.Where(teacher => teacher.UserId != null && teacher.Status == TeacherStatus.Active),
                lesson => lesson.TeacherId, teacher => teacher.Id, (lesson, teacher) => lesson)
            .Join(db.Students, lesson => lesson.StudentId, student => student.Id,
                (lesson, student) => new { Lesson = lesson, Student = student })
            .Join(db.Instruments, x => x.Lesson.InstrumentId, instrument => instrument.Id,
                (x, instrument) => new { x.Lesson, x.Student, Instrument = instrument })
            .OrderBy(x => x.Lesson.StartAt)
            .Select(x => new
            {
                x.Lesson.Id,
                x.Lesson.TeacherId,
                x.Lesson.StartAt,
                StudentName = x.Student.FirstName + " " + x.Student.LastName,
                InstrumentName = x.Instrument.Name,
            })
            .ToListAsync(cancellationToken);

        var asked = 0;
        foreach (var lesson in pending)
        {
            // NotifyTeacherAsync aynı ders için ikinci satırı açmaz - tur istediği kadar
            // tekrarlanabilir.
            if (await notifier.NotifyTeacherAsync(
                    lesson.TeacherId, StaffNotificationType.AttendanceMissing, Title,
                    Body(lesson.StudentName, lesson.InstrumentName, lesson.StartAt, clock),
                    ReferenceType, lesson.Id))
                asked++;
        }

        // Artık yoklama beklemeyen derslerin açık sorularını kapat (ders iptal edildi, yoklama
        // başka bir yoldan girildi). Pencere burada UYGULANMAZ: eski ama hâlâ cevapsız soru kalır.
        var openLessonIds = await notifier.OpenReminderReferenceIdsAsync(StaffNotificationType.AttendanceMissing, ReferenceType);
        var stillAwaiting = await AwaitingAttendance(db)
            .Where(lesson => openLessonIds.Contains(lesson.Id))
            .Select(lesson => lesson.Id)
            .ToListAsync(cancellationToken);
        var cleared = 0;
        foreach (var lessonId in openLessonIds.Except(stillAwaiting))
            cleared += await notifier.ClearReminderForAllAsync(StaffNotificationType.AttendanceMissing, ReferenceType, lessonId);

        await db.SaveChangesAsync(cancellationToken);
        return new Result(asked, cleared);
    }

    // Yoklama bekleyen ders: planlandığı gibi (normal ya da telafi) duruyor ve yoklaması yok.
    // Yoklama girilince ders Completed'a geçer, iptal edilen ders Cancelled olur - ikisi de düşer.
    private static IQueryable<Lesson> AwaitingAttendance(AbderaDbContext db) =>
        db.Lessons
            .Where(lesson => lesson.Status == LessonStatus.Normal || lesson.Status == LessonStatus.Makeup)
            .Where(lesson => !db.LessonAttendances.Any(attendance => attendance.LessonId == lesson.Id));

    // Kullanıcıya görünen metin: tarih okulun yerel saatinde ve tr-TR ile (CLAUDE.md - Dockerfile
    // icu-data-full + DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false; LessonNoteReminderJob ile aynı).
    public static string Body(string studentName, string instrumentName, DateTimeOffset startAt, IClock clock)
    {
        var turkish = CultureInfo.GetCultureInfo("tr-TR");
        var when = clock.ToSchoolLocal(startAt).ToString("d MMMM dddd HH:mm", turkish);
        return $"{studentName} · {instrumentName} · {when}";
    }
}
