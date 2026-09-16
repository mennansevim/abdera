using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations;

// Okulu temiz bir sayfayla açmak için tüm operasyonel veriyi kaldırır: öğretmen, öğrenci,
// veli, ders, aidat, tahsilat, gösteri ve bunlara bağlı her şey. Geriye yalnızca Admin
// kullanıcısı ve YAPILANDIRMA kalır.
//
// KORUNAN tablolar (bilinçli olarak dokunulmuyor):
//   instruments, skill_definitions, message_templates  -> referans veri, migration ile gelir
//   tuition_rates, billing_settings, prepay_discount_tiers -> okulun ücret politikası
//   notification_automation_settings, instrument_maintenance_settings, school_calendar_days
//   backup_runs, system_health_status, DataProtectionKeys -> altyapı; oturum çerezlerini
//                                                            geçersiz kılmamak için kalır
//
// Bu dosya elle yazıldı (model değişikliği yok), `TrimDemoData`/`ResetLargeDemoData`/
// `ResetResidualMockData` ile aynı desende. Boş bir veritabanında hiçbir şey silmez, yani
// yeni kurulumlarda zararsızdır.
//
// DİKKAT: geri alınamaz. Down() bilerek boştur - silinen satırlar yeniden kurulamaz.
[DbContext(typeof(AbderaDbContext))]
[Migration("20260916120000_ResetToAdminOnly")]
public sealed class ResetToAdminOnly : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Silme sırası bağımlılık zincirini izler: en uçtaki satırlardan köke doğru.
        migrationBuilder.Sql("""
            DELETE FROM payment_corrections;
            DELETE FROM payments;
            DELETE FROM bank_incoming_transactions;
            DELETE FROM receivables;
            DELETE FROM makeup_credits;

            DELETE FROM practice_assignments;
            DELETE FROM practice_journal_entries;
            DELETE FROM skill_assessments;
            DELETE FROM lesson_notes;
            DELETE FROM lesson_rsvps;
            DELETE FROM lesson_attendances;
            DELETE FROM lesson_change_requests;
            DELETE FROM lessons;
            DELETE FROM lesson_series;
            DELETE FROM enrollments;

            DELETE FROM show_items;
            DELETE FROM show_events;
            DELETE FROM student_photos;

            DELETE FROM instrument_maintenance_reminders;
            DELETE FROM virtual_ibans;
            DELETE FROM guardian_login_codes;
            DELETE FROM student_guardians;

            DELETE FROM teacher_availability;
            DELETE FROM teacher_time_off;
            DELETE FROM teacher_instruments;

            DELETE FROM notification_jobs;
            DELETE FROM staff_notifications;
            DELETE FROM whatsapp_messages;
            DELETE FROM whatsapp_webhook_events;
            DELETE FROM expenses;

            DELETE FROM guardians;
            DELETE FROM students;
            DELETE FROM teachers;

            -- Öğretmenlerin giriş hesapları. Admin(ler) dokunulmadan kalır - aksi hâlde
            -- panele girilecek hiçbir hesap kalmazdı.
            DELETE FROM users WHERE role <> 'Admin';

            -- Artık var olmayan kayıtlara ait denetim satırları. `ResetResidualMockData`
            -- ile aynı yaklaşım: audit tablosunun kendisi korunur, yalnızca ulaşılamaz
            -- hâle gelen satırlar temizlenir. Yapılandırma ve oturum olaylarının izi kalır.
            DELETE FROM audit_log
             WHERE entity_type IN (
                'Student', 'Teacher', 'Guardian', 'Enrollment', 'Lesson', 'LessonSeries',
                'LessonChangeRequest', 'LessonAttendance', 'LessonRsvp', 'LessonNote',
                'Receivable', 'Payment', 'PaymentCorrection', 'MakeupCredit', 'Expense',
                'ShowEvent', 'ShowItem', 'SkillAssessment', 'PracticeAssignment',
                'VirtualIban', 'BankIncomingTransaction', 'NotificationJob');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Geri alınamaz: silinen operasyonel veri yeniden kurulamaz.
    }
}
