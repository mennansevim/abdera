using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Platform.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolLicense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "license_ends_on",
                table: "schools",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "license_starts_on",
                table: "schools",
                type: "date",
                nullable: true);

            // Paketle kurulmuş okulların lisansı kuruluş gününde (İstanbul) başlamış sayılır; paketi olmayan
            // (sahiplenilmiş) okulda lisans tarihi tutulmaz.
            migrationBuilder.Sql("""
                UPDATE schools
                SET license_starts_on = (created_at AT TIME ZONE 'Europe/Istanbul')::date,
                    license_ends_on = ((created_at AT TIME ZONE 'Europe/Istanbul')::date + INTERVAL '1 year' - INTERVAL '1 day')::date
                WHERE staff_seat_limit IS NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_schools_license_period",
                table: "schools",
                sql: "(license_starts_on IS NULL AND license_ends_on IS NULL) OR license_ends_on >= license_starts_on");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_schools_license_period",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "license_ends_on",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "license_starts_on",
                table: "schools");
        }
    }
}
