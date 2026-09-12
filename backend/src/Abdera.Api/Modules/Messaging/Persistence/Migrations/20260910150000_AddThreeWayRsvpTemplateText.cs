using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Messaging.Persistence.Migrations;

// Mevcut kurulumlarda lesson_reminder_rsvp satırı daha eski, iki seçenekli metni
// taşıyor. Quick-reply payload'ları zaten üçlü gönderiliyor; görünen mesaj geçmişi ve
// yönetici şablon ekranı da Meta'daki üç butonla aynı seçenekleri anlatsın.
[DbContext(typeof(AbderaDbContext))]
[Migration("20260910150000_AddThreeWayRsvpTemplateText")]
public class AddThreeWayRsvpTemplateText : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE message_templates
            SET body = E'🎹 Ders Hatırlatması\n\nMerhaba {{guardian_name}},\n\n{{student_name}} öğrencimizin {{instrument}} dersi bugün\n{{lesson_time}} saatinde.\n\nÖğretmen: {{teacher_name}}\n\nKatılım durumunuzu bildirir misiniz?\n\nHızlı yanıtlar: ✅ Geliyorum   🕒 Geç kalacağım   ❌ Gelemiyorum'
            WHERE name = 'lesson_reminder_rsvp';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE message_templates
            SET body = E'🎹 Ders Hatırlatması\n\nMerhaba {{guardian_name}},\n\n{{student_name}} öğrencimizin {{instrument}} dersi bugün\n{{lesson_time}} saatinde.\n\nÖğretmen: {{teacher_name}}\n\nKatılım durumunuzu bildirir misiniz?\n\nHızlı yanıtlar: ✅ Geliyorum   ❌ Gelemiyorum'
            WHERE name = 'lesson_reminder_rsvp';
            """);
    }
}
