using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Platform.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolCustomDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "custom_domain",
                table: "schools",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "custom_domain_active",
                table: "schools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "custom_domain_check",
                table: "schools",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "custom_domain_checked_at",
                table: "schools",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "custom_domain_redirect",
                table: "schools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_schools_custom_domain",
                table: "schools",
                column: "custom_domain",
                unique: true,
                filter: "custom_domain IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_schools_custom_domain",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "custom_domain",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "custom_domain_active",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "custom_domain_check",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "custom_domain_checked_at",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "custom_domain_redirect",
                table: "schools");
        }
    }
}
