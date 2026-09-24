using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// Ders değişikliği bildirimleri - kullanıcı istekleri: "takvimde ders taşındığında ilgili
// öğretmenin ekranına bildirim gitsin" ve "ders telafi / saat değişimi / iptal durumlarında
// bildirim gelsin, mail gelsin". Alıcı: dersin öğretmeni + yöneticiler, işlemi yapan hariç
// (IStaffNotifier.NotifyLessonStaffAsync). E-posta kuyruğa alınır; çağıran handler
// SaveChanges'ten sonra IStaffNotifier.FlushEmailsAsync'i çağırır.
//
// Metinler tek yerde: ders birden fazla yoldan taşınabiliyor (sürükle-bırak onayı, ders
// detayından düzenleme) ve hepsinde aynı cümle görünmeli.
internal static class LessonChangeNotice
{
    // Tarih/saat okulun yerel saatinde ve tr-TR ile yazılır (CLAUDE.md: kullanıcıya görünen
    // metinde açık kültür; Dockerfile icu-data-full + DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false).
    public static async Task NotifyMovedAsync(
        IStaffNotifier notifier,
        AbderaDbContext db,
        IClock clock,
        Guid? actorUserId,
        Guid teacherId,
        Guid studentId,
        DateTimeOffset previousStartAt,
        DateTimeOffset newStartAt,
        Guid newLessonId,
        string? extraNote = null)
    {
        var body = $"{await StudentNameAsync(db, studentId)} · {Format(clock, previousStartAt)} → {Format(clock, newStartAt)}";
        if (!string.IsNullOrWhiteSpace(extraNote)) body += $" · {extraNote}";

        await notifier.NotifyLessonStaffAsync(
            teacherId, actorUserId, StaffNotificationType.LessonMoved, "Ders saati değişti", body, "lesson", newLessonId);
    }

    public static async Task NotifyCancelledAsync(
        IStaffNotifier notifier,
        AbderaDbContext db,
        IClock clock,
        Guid? actorUserId,
        Guid teacherId,
        Guid studentId,
        DateTimeOffset startAt,
        Guid lessonId,
        bool makeupCreditEarned,
        string? reason)
    {
        var body = $"{await StudentNameAsync(db, studentId)} · {Format(clock, startAt)} · " +
                   (makeupCreditEarned ? "telafi hakkı tanındı" : "telafisiz");
        if (!string.IsNullOrWhiteSpace(reason)) body += $" · {reason.Trim()}";

        await notifier.NotifyLessonStaffAsync(
            teacherId, actorUserId, StaffNotificationType.LessonCancelled, "Ders iptal edildi", body, "lesson", lessonId);
    }

    public static async Task NotifyMakeupScheduledAsync(
        IStaffNotifier notifier,
        AbderaDbContext db,
        IClock clock,
        Guid? actorUserId,
        Guid teacherId,
        Guid studentId,
        DateTimeOffset startAt,
        Guid makeupLessonId)
    {
        var body = $"{await StudentNameAsync(db, studentId)} · {Format(clock, startAt)}";

        await notifier.NotifyLessonStaffAsync(
            teacherId, actorUserId, StaffNotificationType.MakeupScheduled, "Telafi dersi planlandı", body, "lesson", makeupLessonId);
    }

    private static async Task<string> StudentNameAsync(AbderaDbContext db, Guid studentId) =>
        await db.Students
            .Where(student => student.Id == studentId)
            .Select(student => student.FirstName + " " + student.LastName)
            .SingleOrDefaultAsync() ?? "Öğrenci";

    private static string Format(IClock clock, DateTimeOffset instant) =>
        clock.ToSchoolLocal(instant).ToString("d MMMM dddd HH:mm", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
}
