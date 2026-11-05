using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations
{
    // docs/10-decisions.md AK: veli "gelemiyor"/"gecikecek" dediğinde dersin öğretmenine WhatsApp.
    // teachers.phone_number boş olabilir (numarası olmayan öğretmen yalnızca ekran içi bildirimi
    // alır). teacher_rsvp_alert gövdesi docs/06-whatsapp.md'deki metin - Meta onayı bekler (D2).
    /// <inheritdoc />
    public partial class AddTeacherPhoneAndRsvpAlertTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "phone_number",
                table: "teachers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql("""
                INSERT INTO message_templates (id, name, language, body, is_active) VALUES
                (gen_random_uuid(), 'teacher_rsvp_alert', 'tr', E'🔔 Veli Yanıtı\n\nMerhaba {{teacher_name}},\n\n{{student_name}} öğrencinizin {{lesson_time}} dersi için veli yanıtı: {{rsvp_answer}}.\n\nAyrıntılar okul takviminde.', true)
                ON CONFLICT (name) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM message_templates WHERE name = 'teacher_rsvp_alert';");

            migrationBuilder.DropColumn(
                name: "phone_number",
                table: "teachers");
        }
    }
}
