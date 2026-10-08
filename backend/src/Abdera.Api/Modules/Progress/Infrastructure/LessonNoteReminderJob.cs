using System.Globalization;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Progress.Features;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Abdera.Api.Modules.Progress.Infrastructure;

// Yorum bekleyen dersleri öğretmenin zilindeki hatırlatmaya dönüştürür
// (StaffNotificationType.LessonNoteMissing). Öğretmen başına TEK satır: "3 dersin notu eksik"
// her ders için ayrı bir bildirim olsaydı günde beş ders veren bir öğretmenin zili bununla
// dolardı. Satır iş bitene kadar tazelenir ve en fazla ÜÇ GÜNDE BİR (okulun yerel takvimiyle)
// okunmamışa döner; öğretmen eksikleri kapatınca kendiliğinden okundu sayılır. Kullanıcı
// isteği: "not hatırlatması her gün olmasın, 3 günde bir olabilir".
//
// BillingDailyJob kalıbı: kalıcı container'da LessonNoteReminderWorker, serverless yayında
// (Runtime:Serverless=true) Vercel Cron -> LessonNoteReminderCron aynı RunAsync'i çağırır.
// Yalnızca ekran içi bildirim - e-posta gönderilmez (yinelenen bir dürtü için e-posta
// fazla; ders değişikliği e-postaları M4'teki tek seferlik olaylar için).
public static class LessonNoteReminderJob
{
    // Referans öğretmenin kendisi: UNIQUE (user_id, type, reference_type, reference_id)
    // öğretmen başına tek hatırlatma satırını veritabanı düzeyinde de garanti eder.
    // PersonEraser öğretmen silinirken reference_id = teacher_id satırlarını zaten temizliyor.
    public const string ReferenceType = "teacher";
    // Okunmuş hatırlatma en son öne çıktığı yerel günden bu kadar gün sonra yeniden açılır
    // (pazartesi düştüyse perşembe). Ekrandaki "Yorum bekleyen dersler" kartı her gün görünür
    // kalır; seyrekleşen yalnızca zildeki dürtü.
    public const int ResurfaceEveryDays = 3;
    private const int NamedLessonsInBody = 3;
    // staff_notifications.body HasMaxLength(500) ile birebir - uzun öğrenci adları sütunu
    // taşırırsa tur DbUpdateException ile düşerdi.
    private const int MaxBodyLength = 500;

    public record Result(int TeachersWithPendingNotes, int PendingLessonCount, int RemindedCount, int ClearedCount);

    public static async Task<Result> RunAsync(
        AbderaDbContext db, IClock clock, IStaffNotifier notifier, CancellationToken cancellationToken = default)
    {
        try
        {
            return await RunOnceAsync(db, clock, notifier, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Aynı anda iki tur (worker + cron) aynı öğretmen için ilk satırı açmaya çalıştıysa
            // UNIQUE ikincisini reddeder; yeniden kurulan turda satır artık var ve güncellenir.
            db.ChangeTracker.Clear();
            return await RunOnceAsync(db, clock, notifier, cancellationToken);
        }
    }

    private static async Task<Result> RunOnceAsync(
        AbderaDbContext db, IClock clock, IStaffNotifier notifier, CancellationToken cancellationToken)
    {
        // "Üç günde bir dürt" sınırı okulun yerel takvimine göre: iki gün önceki yerel günün
        // (Europe/Istanbul) başlangıcından önce son kez öne çıkmış hatırlatma yeniden okunmamışa döner.
        var todayLocal = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var resurfaceThresholdUtc = LessonGenerator.ToUtcInstant(
            todayLocal.AddDays(-(ResurfaceEveryDays - 1)), TimeOnly.MinValue, clock.SchoolTimeZone);

        // Giriş hesabı olmayan öğretmenin zili yok (IStaffNotifier de satır açmazdı).
        var teacherIds = await db.Teachers
            .Where(teacher => teacher.UserId != null && teacher.Status == TeacherStatus.Active)
            .Select(teacher => teacher.Id)
            .ToListAsync(cancellationToken);

        int teachersWithPending = 0, pendingLessons = 0, reminded = 0, cleared = 0;
        foreach (var teacherId in teacherIds)
        {
            var pending = await PendingLessonNotes.ListForTeacherAsync(db, clock, teacherId, cancellationToken);
            if (pending.Count == 0)
            {
                if (await notifier.ClearTeacherReminderAsync(
                        teacherId, StaffNotificationType.LessonNoteMissing, ReferenceType, teacherId))
                    cleared++;
                continue;
            }

            teachersWithPending++;
            pendingLessons += pending.Count;
            if (await notifier.RemindTeacherAsync(
                    teacherId, StaffNotificationType.LessonNoteMissing, Title(pending.Count), Body(pending, clock),
                    ReferenceType, teacherId, resurfaceIfSurfacedBefore: resurfaceThresholdUtc))
                reminded++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return new Result(teachersWithPending, pendingLessons, reminded, cleared);
    }

    // Not kaydedilince ya da silinince çağrılır (LessonNotes): zildeki hatırlatma bir sonraki
    // turu beklemeden güncel listeyi anlatır. Son eksik de kapandıysa okundu sayılır; kalan
    // varsa metni tazelenir ama okunmamışa DÖNMEZ - aksi hâlde notu yazılmış dersler zilde
    // üç saate kadar "yorumu bekliyor" diye sayılmaya devam ediyordu. Hatırlatma AÇMAZ - o,
    // turun işi. SaveChanges çağırmaz (IStaffNotifier sözleşmesi); kaydetmek çağıranda.
    /// <returns>Hatırlatma satırı değiştiyse true.</returns>
    public static async Task<bool> RefreshForTeacherAsync(AbderaDbContext db, IClock clock, IStaffNotifier notifier, Guid teacherId)
    {
        var pending = await PendingLessonNotes.ListForTeacherAsync(db, clock, teacherId);
        if (pending.Count == 0)
            return await notifier.ClearTeacherReminderAsync(
                teacherId, StaffNotificationType.LessonNoteMissing, ReferenceType, teacherId);
        return await notifier.RefreshTeacherReminderTextAsync(
            teacherId, StaffNotificationType.LessonNoteMissing, Title(pending.Count), Body(pending, clock),
            ReferenceType, teacherId);
    }

    public static string Title(int pendingCount) =>
        pendingCount == 1 ? "1 dersin yorumu bekliyor" : $"{pendingCount} dersin yorumu bekliyor";

    // Kullanıcıya görünen metin: tarih okulun yerel saatinde ve tr-TR ile (CLAUDE.md - Dockerfile
    // icu-data-full + DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false; LessonChangeNotice ile aynı).
    // Liste en eskiden yeniye geldiği için ilk sıradakiler en çok bekleyenler.
    public static string Body(IReadOnlyList<PendingLessonNotes.PendingLessonNoteResponse> pending, IClock clock)
    {
        const string whereToAdd = "Gelişim ekranındaki \"Yorum bekleyen dersler\" kartından ekleyebilirsin.";
        // Kültür burada çözülür, statik alanda değil: ICU yoksa statik başlatıcı sınıfı (ve not
        // kaydında çağrılan RefreshForTeacherAsync'i) tamamen kullanılamaz hâle getirirdi.
        var turkish = CultureInfo.GetCultureInfo("tr-TR");
        var named = pending
            .Take(NamedLessonsInBody)
            .Select(lesson => $"{lesson.StudentName} ({clock.ToSchoolLocal(lesson.StartAt).ToString("d MMMM", turkish)})");
        var rest = pending.Count - NamedLessonsInBody;
        var list = string.Join(", ", named) + (rest > 0 ? $" ve {rest} ders daha" : "");

        var body = $"Tamamlanan derslerin notunu girmeyi unutma: {list}. {whereToAdd}";
        return body.Length <= MaxBodyLength
            ? body
            : $"Tamamlanan {pending.Count} dersin notunu girmeyi unutma. {whereToAdd}";
    }
}
