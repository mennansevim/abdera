using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.People.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuardianIssuedPassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "issued_password_protected",
                table: "guardians",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "password_set_by_guardian",
                table: "guardians",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Portaldan şifresini değiştirmiş veliler (audit: guardian.password_changed) okulun
            // göremeyeceği bir şifre kullanıyor - "Veli girişleri" ekranı bunu doğru göstersin.
            migrationBuilder.Sql("""
                UPDATE guardians g SET password_set_by_guardian = true
                WHERE g.password_hash IS NOT NULL
                  AND EXISTS (SELECT 1 FROM audit_log a
                              WHERE a.action = 'guardian.password_changed' AND a.entity_id = g.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "issued_password_protected",
                table: "guardians");

            migrationBuilder.DropColumn(
                name: "password_set_by_guardian",
                table: "guardians");
        }
    }
}
