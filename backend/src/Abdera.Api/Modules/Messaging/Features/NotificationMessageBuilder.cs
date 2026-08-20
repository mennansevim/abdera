using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Features;

public record BuiltMessage(string TemplateName, IReadOnlyDictionary<string, string> Parameters);

// NotificationJob'ın tipine ve reference_id'sine bakarak hangi şablonun hangi parametrelerle
// gönderileceğini çözer. docs/06-whatsapp.md lesson_reminder_rsvp'nin tam gövdesini veriyor;
// diğer üçü (lesson_rescheduled, makeup_approved, payment_reminder) aynı D2 mantığıyla -
// Fake ile geliştirilip Meta onayını paralel bekleyen - kendi şablonlarımız.
public static class NotificationMessageBuilder
{
    public static async Task<BuiltMessage?> BuildAsync(NotificationJob job, AbderaDbContext db, IClock clock)
    {
        return job.Type switch
        {
            NotificationJobType.LessonReminder => await BuildLessonMessageAsync(job, "lesson_reminder_rsvp", db, clock),
            NotificationJobType.LessonRescheduled => await BuildLessonMessageAsync(job, "lesson_rescheduled", db, clock),
            NotificationJobType.MakeupApproved => await BuildLessonMessageAsync(job, "makeup_approved", db, clock),
            NotificationJobType.PaymentReminder => await BuildPaymentMessageAsync(job, db, clock),
            // ARC-2: Birthday/PackageEnding tanımlı ama hiçbir use-case tarafından
            // üretilmiyor (Faz 7'ye kaldı) - sessizce null dönüp yanıltıcı bir "kayıt
            // bulunamadı" hatasına düşmek yerine dispatcher'ın yakalayıp okunur bir
            // LastError yazabileceği özel bir istisna fırlatılır.
            NotificationJobType.Birthday or NotificationJobType.PackageEnding =>
                throw new NotImplementedNotificationTypeException(job.Type),
            _ => null,
        };
    }

    private static async Task<BuiltMessage?> BuildLessonMessageAsync(NotificationJob job, string templateName, AbderaDbContext db, IClock clock)
    {
        var lesson = await db.Lessons.SingleOrDefaultAsync(l => l.Id == job.ReferenceId);
        if (lesson is null) return null;

        var student = await db.Students.SingleAsync(s => s.Id == lesson.StudentId);
        var teacher = await db.Teachers.SingleAsync(t => t.Id == lesson.TeacherId);
        var instrument = await db.Instruments.SingleAsync(i => i.Id == lesson.InstrumentId);
        var guardian = await db.Guardians.SingleAsync(g => g.PhoneNumber == job.RecipientPhoneNumber);

        var localStart = clock.ToSchoolLocal(lesson.StartAt);
        var parameters = new Dictionary<string, string>
        {
            ["guardian_name"] = guardian.FirstName,
            ["student_name"] = $"{student.FirstName} {student.LastName}",
            ["instrument"] = instrument.Name,
            ["lesson_time"] = localStart.ToString("dd MMMM HH:mm", new System.Globalization.CultureInfo("tr-TR")),
            ["new_lesson_time"] = localStart.ToString("dd MMMM HH:mm", new System.Globalization.CultureInfo("tr-TR")),
            ["teacher_name"] = $"{teacher.FirstName} {teacher.LastName}",
        };

        return new BuiltMessage(templateName, parameters);
    }

    private static async Task<BuiltMessage?> BuildPaymentMessageAsync(NotificationJob job, AbderaDbContext db, IClock clock)
    {
        var receivable = await db.Receivables.SingleOrDefaultAsync(r => r.Id == job.ReferenceId);
        if (receivable is null) return null;

        var enrollment = await db.Enrollments.SingleAsync(e => e.Id == receivable.EnrollmentId);
        var student = await db.Students.SingleAsync(s => s.Id == enrollment.StudentId);
        var guardian = await db.Guardians.SingleAsync(g => g.PhoneNumber == job.RecipientPhoneNumber);

        // Not: bu kültür-bağımlı biçimlendirme kasıtlı ve güvenli - kullanıcıya (veliye)
        // gösterilecek bir WhatsApp metni oluşturuyoruz, JSON değil. CLAUDE.md'nin
        // "InvariantCulture kullan" kuralı yalnızca makine-makine veriler (jsonb) içindir;
        // burada açıkça tr-TR istiyoruz ("2.000,00" - Türk okuyucunun beklediği biçim).
        var turkishCulture = new System.Globalization.CultureInfo("tr-TR");
        var parameters = new Dictionary<string, string>
        {
            ["guardian_name"] = guardian.FirstName,
            ["student_name"] = $"{student.FirstName} {student.LastName}",
            ["period"] = receivable.Period,
            ["amount"] = receivable.Amount.ToString("N2", turkishCulture),
            ["currency"] = receivable.Currency,
            ["due_date"] = receivable.DueDate.ToString("dd MMMM yyyy", turkishCulture),
        };

        return new BuiltMessage("payment_reminder", parameters);
    }
}
