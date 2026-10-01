using System.Text.Json;
using Abdera.Api.Modules.Auth.Domain;
using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Billing.Features;

// docs/10-decisions.md A2 (mazeretli satırı): yoklamada "Mazeretli" işaretlenen ders öğrenciye
// bir telafi hakkı doğurur. Hak, dersin gününden sonraki `Policy:ExcusedMakeupWindowDays`
// (varsayılan 21) gün boyunca geçerlidir - telafi dersi de bu pencerenin içine planlanır
// (MakeupCredits.UseAsync). Habersiz gelmeme (Absent) yine kredi doğurmaz.
//
// Yoklama düzeltilebildiği için iki yönlüdür: Mazeretli'ye düzeltilen derse hak açılır,
// Mazeretli'den çıkan dersin henüz kullanılmamış hakkı geri alınır (silinmez, Revoked olur).
// Attendance modülü bu sınıfı açık bir çağrıyla kullanır; makeup_credits'e kendisi yazmaz.
public static class ExcusedMakeupCredit
{
    public static async Task SyncAsync(
        AbderaDbContext db, Lesson lesson, bool isExcused, IClock clock, IConfiguration config, Guid? actorUserId)
    {
        var now = clock.UtcNow;
        var existing = await db.MakeupCredits
            .Where(credit => credit.SourceLessonId == lesson.Id && credit.StudentId == lesson.StudentId)
            .ToListAsync();

        if (isExcused)
        {
            // Aynı derse ikinci hak açılmaz - Mazeretli → Geldi → Mazeretli düzeltmesinde
            // geri alınan hak yerine yenisi açılır, ama hâlâ geçerli/kullanılmış bir hak varsa dokunulmaz.
            if (existing.Any(credit => credit.Status is MakeupCreditStatus.Available or MakeupCreditStatus.Used))
                return;

            var windowDays = config.GetValue("Policy:ExcusedMakeupWindowDays", 21);
            var lessonDate = DateOnly.FromDateTime(clock.ToSchoolLocal(lesson.StartAt).Date);
            // Pencerenin son günü dahil: o günün bitişine kadar planlanabilir.
            var expiresAt = LessonGenerator.ToUtcInstant(lessonDate.AddDays(windowDays + 1), TimeOnly.MinValue, clock.SchoolTimeZone);
            var credit = MakeupCredit.Earn(lesson.StudentId, lesson.Id, MakeupCreditEarnedReason.Excused, now, expiresAt);
            db.MakeupCredits.Add(credit);
            db.AuditLogs.Add(AuditLog.Record(
                actorUserId, "makeup_credit.earned_excused", nameof(MakeupCredit), credit.Id, now,
                afterJson: JsonSerializer.Serialize(new { SourceLessonId = lesson.Id, credit.ExpiresAt })));
            return;
        }

        foreach (var credit in existing.Where(credit => credit.EarnedReason == MakeupCreditEarnedReason.Excused))
        {
            if (!credit.Revoke()) continue;
            db.AuditLogs.Add(AuditLog.Record(
                actorUserId, "makeup_credit.revoked", nameof(MakeupCredit), credit.Id, now,
                JsonSerializer.Serialize(new { Status = MakeupCreditStatus.Available.ToString() }),
                JsonSerializer.Serialize(new { Status = credit.Status.ToString(), Reason = "Yoklama Mazeretli olmaktan çıkarıldı." })));
        }
    }
}
