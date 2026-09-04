using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Scheduling.Features;

// "Takvimde ders taşındığında ilgili öğretmenin ekranına bildirim gitsin" (kullanıcı isteği).
// Ders iki ayrı yoldan taşınabiliyor - takvimden sürükle-bırak (ChangeRequests.ApproveAsync)
// ve ders detayından düzenleme (UpdateLesson) - metin ikisinde de aynı olsun diye burada.
internal static class LessonMovedNotice
{
    // Bildirimi öğretmenin okuyacağı biçimde kurar: tarih/saat okulun yerel saatinde ve
    // tr-TR ile yazılır (CLAUDE.md: kullanıcıya görünen metinde açık kültür; Dockerfile
    // icu-data-full + DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false ile bunu destekliyor).
    public static async Task<bool> NotifyTeacherAsync(
        IStaffNotifier notifier,
        AbderaDbContext db,
        IClock clock,
        Guid teacherId,
        Guid studentId,
        DateTimeOffset previousStartAt,
        DateTimeOffset newStartAt,
        Guid newLessonId,
        string? extraNote = null)
    {
        var studentName = await db.Students
            .Where(student => student.Id == studentId)
            .Select(student => student.FirstName + " " + student.LastName)
            .SingleOrDefaultAsync() ?? "Öğrenci";

        var body = $"{studentName} · {Format(clock, previousStartAt)} → {Format(clock, newStartAt)}";
        if (!string.IsNullOrWhiteSpace(extraNote)) body += $" · {extraNote}";

        return await notifier.NotifyTeacherAsync(
            teacherId,
            StaffNotificationType.LessonMoved,
            "Ders saati değişti",
            body,
            "lesson",
            newLessonId);
    }

    private static string Format(IClock clock, DateTimeOffset instant) =>
        clock.ToSchoolLocal(instant).ToString("d MMMM dddd HH:mm", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
}
