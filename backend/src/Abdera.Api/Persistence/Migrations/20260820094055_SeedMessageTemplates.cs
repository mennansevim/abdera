using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations
{
    // docs/06-whatsapp.md - lesson_reminder_rsvp gövdesi dokümandaki tam metin; diğer üçü
    // (lesson_rescheduled, makeup_approved, payment_reminder) aynı üslupla kendi taslağımız -
    // hepsi Meta onayı bekliyor (D2), Fake provider ile geliştirme bu onayı beklemez.
    // ON CONFLICT (name) DO NOTHING - abdera-migration skill'i: seed migration'lar tekrar
    // çalıştırılabilir olmalı.
    /// <inheritdoc />
    public partial class SeedMessageTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO message_templates (id, name, language, body, is_active) VALUES
                (gen_random_uuid(), 'lesson_reminder_rsvp', 'tr', E'🎹 Ders Hatırlatması\n\nMerhaba {{guardian_name}},\n\n{{student_name}} öğrencimizin {{instrument}} dersi bugün\n{{lesson_time}} saatinde.\n\nÖğretmen: {{teacher_name}}\n\nKatılım durumunuzu bildirir misiniz?\n\nHızlı yanıtlar: ✅ Geliyorum   ❌ Gelemiyorum', true),
                (gen_random_uuid(), 'lesson_rescheduled', 'tr', E'📅 Ders Saati Değişikliği\n\nMerhaba {{guardian_name}},\n\n{{student_name}} öğrencimizin {{instrument}} dersinin saati değişti.\n\nYeni saat: {{new_lesson_time}}\nÖğretmen: {{teacher_name}}\n\nSorularınız için bize yazabilirsiniz.', true),
                (gen_random_uuid(), 'makeup_approved', 'tr', E'✅ Telafi Dersi Onaylandı\n\nMerhaba {{guardian_name}},\n\n{{student_name}} öğrencimizin telafi dersi planlandı.\n\n{{instrument}} - {{lesson_time}}\nÖğretmen: {{teacher_name}}\n\nGörüşmek üzere!', true),
                (gen_random_uuid(), 'payment_reminder', 'tr', E'💳 Aidat Hatırlatması\n\nMerhaba {{guardian_name}},\n\n{{student_name}} öğrencimizin {{period}} dönemi aidatı henüz ödenmedi.\n\nTutar: {{amount}} {{currency}}\nSon ödeme tarihi: {{due_date}}\n\nÖdemenizi yaptıysanız bu mesajı dikkate almayınız.', true)
                ON CONFLICT (name) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM message_templates
                WHERE name IN ('lesson_reminder_rsvp', 'lesson_rescheduled', 'makeup_approved', 'payment_reminder');
                """);
        }
    }
}
