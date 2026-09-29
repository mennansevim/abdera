using Abdera.Api.Modules.Billing.Domain;
using Abdera.Api.Modules.People.Domain;
using Abdera.Api.Modules.Scheduling.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Features;

// docs/00-master-prompt.md: "The first version should support a small set of deterministic
// intents: ders, aidat, telafi, okula yaz." Bir veli birden fazla öğrenciye bağlıysa
// deterministik olarak ilk (StudentGuardian.IsPrimary öncelikli) öğrenciyi baz alır -
// MVP'de öğrenci seçtirme akışı yok (over-engineering'den kaçınma).
public static class DeterministicIntents
{
    // Tanınmayan mesaja (selam, "test", soru) sessiz kalmak veliye "sistem çalışmıyor" dedirtiyordu
    // (gerçek bir canlı test: "Test"/"Q" yazılan mesajlar işlendi ama cevapsız kaldı). Artık
    // mesajın alındığı teyit edilir ve kullanılabilecek komutlar listelenir.
    public const string HelpText =
        "Mesajınızı aldık, okul yönetimi görecek.\n" +
        "Hızlı bilgi için şunlardan birini yazabilirsiniz:\n" +
        "• ders - sonraki dersiniz\n" +
        "• aidat - aidat durumunuz\n" +
        "• telafi - telafi haklarınız\n" +
        "• dur - WhatsApp bildirimlerini kapatır";

    public static async Task<string> ResolveAsync(string normalizedText, Guardian guardian, AbderaDbContext db, IClock clock)
    {
        // "Ders?", "aidat." gibi noktalamalı yazımlar da komut sayılır.
        var command = normalizedText.Trim().TrimEnd('.', '!', '?', ',');
        return command switch
        {
            "ders" => await ResolveNextLessonAsync(guardian, db, clock),
            "aidat" => await ResolveDuesAsync(guardian, db, clock),
            "telafi" => await ResolveMakeupCreditsAsync(guardian, db),
            "okula yaz" => "Mesajınızı aldık, okul yönetimine iletildi.",
            _ => HelpText,
        };
    }

    private static async Task<Guid?> ResolveStudentIdAsync(Guardian guardian, AbderaDbContext db)
    {
        return await db.StudentGuardians
            .Where(sg => sg.GuardianId == guardian.Id)
            .OrderByDescending(sg => sg.IsPrimary)
            .ThenBy(sg => sg.StudentId)
            .Select(sg => (Guid?)sg.StudentId)
            .FirstOrDefaultAsync();
    }

    private static async Task<string> ResolveNextLessonAsync(Guardian guardian, AbderaDbContext db, IClock clock)
    {
        var studentId = await ResolveStudentIdAsync(guardian, db);
        if (studentId is null) return "Kayıtlı bir öğrenci bulamadık.";

        var now = clock.UtcNow;
        var next = await db.Lessons
            .Where(l => l.StudentId == studentId && l.StartAt > now &&
                        (l.Status == LessonStatus.Normal || l.Status == LessonStatus.Makeup))
            .OrderBy(l => l.StartAt)
            .Join(db.Students, l => l.StudentId, s => s.Id, (l, s) => new { Lesson = l, Student = s })
            .Join(db.Teachers, x => x.Lesson.TeacherId, t => t.Id, (x, t) => new { x.Lesson, x.Student, Teacher = t })
            .Join(db.Instruments, x => x.Lesson.InstrumentId, i => i.Id, (x, i) => new { x.Lesson, x.Student, x.Teacher, Instrument = i })
            .FirstOrDefaultAsync();

        if (next is null) return "Planlanmış bir dersiniz bulunmuyor.";

        var localStart = clock.ToSchoolLocal(next.Lesson.StartAt);
        var culture = new System.Globalization.CultureInfo("tr-TR");
        return $"{next.Student.FirstName}'nin sonraki {next.Instrument.Name.ToLower(culture)} dersi " +
               $"{localStart.ToString("d MMMM dddd HH:mm", culture)}.\nÖğretmen: {next.Teacher.FirstName} {next.Teacher.LastName}.";
    }

    private static async Task<string> ResolveDuesAsync(Guardian guardian, AbderaDbContext db, IClock clock)
    {
        var studentId = await ResolveStudentIdAsync(guardian, db);
        if (studentId is null) return "Kayıtlı bir öğrenci bulamadık.";

        var enrollmentIds = await db.Enrollments.Where(e => e.StudentId == studentId).Select(e => e.Id).ToListAsync();
        var today = DateOnly.FromDateTime(clock.ToSchoolLocal(clock.UtcNow).Date);
        var culture = new System.Globalization.CultureInfo("tr-TR");

        var upcoming = await db.Receivables
            .Where(r => enrollmentIds.Contains(r.EnrollmentId) && r.Status != ReceivableStatus.Cancelled)
            .OrderByDescending(r => r.DueDate)
            .FirstOrDefaultAsync();

        if (upcoming is null) return "Kayıtlı bir aidat bilgisi bulunmuyor.";

        if (upcoming.Status is ReceivableStatus.Paid)
        {
            return $"{upcoming.Period} dönemi aidatı ödendi.";
        }

        return $"{upcoming.Period} dönemi aidatınız {(upcoming.Status == ReceivableStatus.Overdue ? "vadesi geçmiş" : "ödenmedi")} " +
               $"- {upcoming.DueDate.ToString("d MMMM", culture)} vade, {upcoming.Amount.ToString("N2", culture)} {upcoming.Currency}.";
    }

    private static async Task<string> ResolveMakeupCreditsAsync(Guardian guardian, AbderaDbContext db)
    {
        var studentId = await ResolveStudentIdAsync(guardian, db);
        if (studentId is null) return "Kayıtlı bir öğrenci bulamadık.";

        var availableCount = await db.MakeupCredits
            .CountAsync(c => c.StudentId == studentId && c.Status == MakeupCreditStatus.Available);

        return availableCount switch
        {
            0 => "Kullanılabilir telafi dersiniz bulunmuyor.",
            1 => "Kullanılabilir 1 telafi dersiniz bulunuyor.",
            _ => $"Kullanılabilir {availableCount} telafi dersiniz bulunuyor.",
        };
    }
}
