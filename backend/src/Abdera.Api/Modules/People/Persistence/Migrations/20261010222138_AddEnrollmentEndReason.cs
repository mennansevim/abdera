using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.People.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentEndReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "end_note",
                table: "enrollments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "end_reason",
                table: "enrollments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_enrollments_end_reason_only_when_ended",
                table: "enrollments",
                sql: "(end_reason IS NULL AND end_note IS NULL) OR status = 'Ended'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_enrollments_end_reason_only_when_ended",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "end_note",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "end_reason",
                table: "enrollments");
        }
    }
}
