using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteRepertoireAndParentComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "parent_comment",
                table: "lesson_notes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "parent_comment_approved_at",
                table: "lesson_notes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_comment_approved_by",
                table: "lesson_notes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "piece_composer",
                table: "lesson_notes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "piece_resource_url",
                table: "lesson_notes",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "piece_resource_visible_to_guardian",
                table: "lesson_notes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "piece_status",
                table: "lesson_notes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "piece_target_date",
                table: "lesson_notes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "lesson_notes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql("UPDATE lesson_notes SET updated_at = created_at;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "updated_at",
                table: "lesson_notes",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "parent_comment",
                table: "lesson_notes");

            migrationBuilder.DropColumn(
                name: "parent_comment_approved_at",
                table: "lesson_notes");

            migrationBuilder.DropColumn(
                name: "parent_comment_approved_by",
                table: "lesson_notes");

            migrationBuilder.DropColumn(
                name: "piece_composer",
                table: "lesson_notes");

            migrationBuilder.DropColumn(
                name: "piece_resource_url",
                table: "lesson_notes");

            migrationBuilder.DropColumn(
                name: "piece_resource_visible_to_guardian",
                table: "lesson_notes");

            migrationBuilder.DropColumn(
                name: "piece_status",
                table: "lesson_notes");

            migrationBuilder.DropColumn(
                name: "piece_target_date",
                table: "lesson_notes");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "lesson_notes");
        }
    }
}
