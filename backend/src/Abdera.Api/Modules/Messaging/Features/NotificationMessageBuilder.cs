using Abdera.Api.Modules.Messaging.Domain;
using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace Abdera.Api.Modules.Messaging.Features;

// ButtonPayloads yalnızca lesson_reminder_rsvp için doldurulur (imzalı/opak referans -
// docs/06-whatsapp.md "Buton payload güvenliği"). Meta Cloud API'de quick-reply butonlarının
// payload'ı gönderim anında template'in "button" component'i üzerinden override edilir; sıra
// önemlidir (index 0/1/2 = Evet / Geç kalacağım / Hayır).
public record BuiltMessage(string TemplateName, IReadOnlyDictionary<string, string> Parameters, IReadOnlyList<string>? ButtonPayloads = null);

// NotificationJob'ın tipine ve reference_id'sine bakarak hangi şablonun hangi parametrelerle
// gönderileceğini çözer. docs/06-whatsapp.md lesson_reminder_rsvp'nin tam gövdesini veriyor;
// diğer üçü (lesson_rescheduled, makeup_approved, payment_reminder) aynı D2 mantığıyla -
// Fake ile geliştirilip Meta onayını paralel bekleyen - kendi şablonlarımız.
public static class NotificationMessageBuilder
{
    public static async Task<BuiltMessage?> BuildAsync(NotificationJob job, AbderaDbContext db, IClock clock, IConfiguration config)
    {
        return job.Type switch
        {
            NotificationJobType.LessonReminder => await BuildLessonMessageAsync(job, "lesson_reminder_rsvp", db, clock, config),
            NotificationJobType.LessonRescheduled => await BuildLessonMessageAsync(job, "lesson_rescheduled", db, clock, config),
            NotificationJobType.MakeupApproved => await BuildLessonMessageAsync(job, "makeup_approved", db, clock, config),
            NotificationJobType.PaymentReminder => await BuildPaymentMessageAsync(job, db, clock),
            NotificationJobType.InstrumentMaintenance => await BuildMaintenanceMessageAsync(job, db),
            // ARC-2: Birthday/PackageEnding tanımlı ama hiçbir use-case tarafından
            // üretilmiyor (Faz 7'ye kaldı) - sessizce null dönüp yanıltıcı bir "kayıt
            // bulunamadı" hatasına düşmek yerine dispatcher'ın yakalayıp okunur bir
            // LastError yazabileceği özel bir istisna fırlatılır.
            NotificationJobType.Birthday or NotificationJobType.PackageEnding =>
                throw new NotImplementedNotificationTypeException(job.Type),
            _ => null,
        };
    }

    private static async Task<BuiltMessage?> BuildMaintenanceMessageAsync(NotificationJob job, AbderaDbContext db)
    {
        var reminder = await db.InstrumentMaintenanceReminders.SingleOrDefaultAsync(item => item.Id == job.ReferenceId);
        if (reminder is null) return null;
        var setting = await db.InstrumentMaintenanceSettings.SingleAsync(item => item.Id == reminder.SettingId);
        var instrument = await db.Instruments.SingleAsync(item => item.Id == setting.InstrumentId);
        var guardian = await db.Guardians.SingleAsync(item => item.Id == reminder.GuardianId);
        return new BuiltMessage("instrument_maintenance_reminder", new Dictionary<string, string>
        {
            ["guardian_name"] = guardian.FirstName,
            ["instrument"] = instrument.Name,
            ["maintenance_type"] = setting.MaintenanceType,
        });
    }

    private static async Task<BuiltMessage?> BuildLessonMessageAsync(NotificationJob job, string templateName, AbderaDbContext db, IClock clock, IConfiguration config)
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

        IReadOnlyList<string>? buttonPayloads = null;
        if (templateName == "lesson_reminder_rsvp")
        {
            var signingKey = config["WhatsApp:PayloadSigningKey"] ?? "";
            var settings = await NotificationAutomationSettings.GetCurrentAsync(db);
            var actions = settings.AllowAttendingLateResponse
                ? new[] { RsvpButtonPayload.AttendingAction, RsvpButtonPayload.AttendingLateAction, RsvpButtonPayload.NotAttendingAction }
                : new[] { RsvpButtonPayload.AttendingAction, RsvpButtonPayload.NotAttendingAction };
            buttonPayloads = actions.Select(action => RsvpButtonPayload.Sign(action, lesson.Id, signingKey)).ToList();
        }

        return new BuiltMessage(templateName, parameters, buttonPayloads);
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
