using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.People.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GuardianNotificationConsentDefaultOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "notification_consent",
                table: "guardians",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            // WhatsApp bildirimi artık açık bir seçim (künyedeki kutu, varsayılan kapalı). Buraya
            // kadar hiçbir veliden gerçek onay toplanmadı (sağlayıcı Disabled'dı), eski "true"
            // yalnızca kolon varsayılanıydı: herkes kapalıya çekilir, yönetici tek tek açar.
            // Rıza değişimi audit_log'a yazılır (CLAUDE.md); sistem kaynaklı olduğu için actor null.
            migrationBuilder.Sql("""
                INSERT INTO audit_log (id, actor_user_id, action, entity_type, entity_id, before_json, after_json, created_at)
                SELECT gen_random_uuid(), NULL, 'guardian.notification_consent_changed', 'Guardian', id,
                       '{"notificationConsent":true}'::jsonb, '{"notificationConsent":false}'::jsonb, now()
                FROM guardians WHERE notification_consent;
                """);
            migrationBuilder.Sql(
                "UPDATE guardians SET notification_consent = false, consent_updated_at = now(), updated_at = now() WHERE notification_consent;");
            // Onayı kapanan velilere kurulmuş bekleyen job'lar gönderilmesin (silinmez, iptal edilir).
            migrationBuilder.Sql(
                "UPDATE notification_jobs SET status = 'Cancelled', updated_at = now() WHERE status IN ('Pending', 'Processing');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Yalnızca kolon varsayılanı geri alınır; kapatılan onaylar ve iptal edilen job'lar
            // geri açılmaz - kimin gerçekten onay verdiği bilinmiyor.
            migrationBuilder.AlterColumn<bool>(
                name: "notification_consent",
                table: "guardians",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);
        }
    }
}
