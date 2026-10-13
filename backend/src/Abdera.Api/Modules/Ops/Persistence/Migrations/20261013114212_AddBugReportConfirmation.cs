using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Ops.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBugReportConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "feedback_at",
                table: "bug_reports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "feedback_note",
                table: "bug_reports",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolved_at",
                table: "bug_reports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_bug_reports_created_by_user_id_status",
                table: "bug_reports",
                columns: new[] { "created_by_user_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_bug_reports_created_by_user_id_status",
                table: "bug_reports");

            migrationBuilder.DropColumn(
                name: "feedback_at",
                table: "bug_reports");

            migrationBuilder.DropColumn(
                name: "feedback_note",
                table: "bug_reports");

            migrationBuilder.DropColumn(
                name: "resolved_at",
                table: "bug_reports");
        }
    }
}
