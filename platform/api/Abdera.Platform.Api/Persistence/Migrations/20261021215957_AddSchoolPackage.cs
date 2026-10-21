using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Platform.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolPackage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "active_staff_users",
                table: "telemetry_snapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "addon_prices_json",
                table: "schools",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "schools",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "TRY");

            migrationBuilder.AddColumn<decimal>(
                name: "price_per_seat",
                table: "schools",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "staff_seat_limit",
                table: "schools",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_schools_price_per_seat",
                table: "schools",
                sql: "price_per_seat IS NULL OR price_per_seat >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_schools_staff_seat_limit",
                table: "schools",
                sql: "staff_seat_limit IS NULL OR staff_seat_limit BETWEEN 1 AND 1000");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_schools_price_per_seat",
                table: "schools");

            migrationBuilder.DropCheckConstraint(
                name: "CK_schools_staff_seat_limit",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "active_staff_users",
                table: "telemetry_snapshots");

            migrationBuilder.DropColumn(
                name: "addon_prices_json",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "price_per_seat",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "staff_seat_limit",
                table: "schools");
        }
    }
}
