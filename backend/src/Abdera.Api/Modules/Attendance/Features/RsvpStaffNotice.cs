using Abdera.Api.Modules.Attendance.Domain;
using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Modules.Messaging.Features;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Attendance.Features;

// Kullanıcı isteği: "Gelemiyor ya da Biraz gecikeceğiz cevabı gelirse ilgili öğretmene bildirim
// olarak gitsin." Veli yanıtı üç yoldan değişir (WhatsApp butonu, veli portalı, yöneticinin elle
// girişi); üçü de buradan geçer ki öğretmen hangi kanaldan gelirse gelsin aynı bildirimi alsın.
//
// Ders başına TEK satır (UNIQUE user+type+reference): yanıt değiştikçe aynı satır tazelenir ve
// okunmamışa döner. "Geliyor" yalnızca önceki yanıt olumsuzken bildirilir - öğretmen "gelemiyor"
// bilgisini almışsa düzeltmeyi de almalı; ilk yanıtı zaten "geliyor" olan ders gürültü yaratmaz.
// Kaydı ekler ama SaveChanges ÇAĞIRMAZ (IStaffNotifier sözleşmesi) - çağıran handler kaydeder.
internal static class RsvpStaffNotice
{
    public static async Task NotifyAsync(
        IStaffNotifier notifier, AbderaDbContext db, IClock clock, Guid lessonId,
        RsvpResponse previous, RsvpResponse current)
    {
        if (previous == current) return;

        var title = current switch
        {
            RsvpResponse.NotAttending => "Veli yanıtı: gelemiyor",
            RsvpResponse.AttendingLate => "Veli yanıtı: biraz gecikecek",
            RsvpResponse.Attending when NeedsTeacherAttention(previous) => "Veli yanıtını değiştirdi: geliyor",
            _ => null,
        };
        if (title is null) return;

        var lesson = await db.Lessons
            .Where(item => item.Id == lessonId)
            .Select(item => new { item.TeacherId, item.StudentId, item.StartAt })
            .SingleOrDefaultAsync();
        if (lesson is null) return;

        var studentName = await db.Students
            .Where(student => student.Id == lesson.StudentId)
            .Select(student => student.FirstName + " " + student.LastName)
            .SingleOrDefaultAsync() ?? "Öğrenci";
        // Kullanıcıya görünen metin: okulun yerel saati ve açık tr-TR (CLAUDE.md).
        var when = clock.ToSchoolLocal(lesson.StartAt)
            .ToString("d MMMM dddd HH:mm", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));

        await notifier.RemindTeacherAsync(
            lesson.TeacherId, StaffNotificationType.GuardianRsvp, title, $"{studentName} · {when}",
            "lesson", lessonId, resurfaceIfSurfacedBefore: DateTimeOffset.MaxValue);
    }

    // Takvimdeki ünlem işaretiyle aynı koşul (frontend: rsvpNeedsAttention).
    private static bool NeedsTeacherAttention(RsvpResponse response) =>
        response is RsvpResponse.NotAttending or RsvpResponse.AttendingLate;
}
