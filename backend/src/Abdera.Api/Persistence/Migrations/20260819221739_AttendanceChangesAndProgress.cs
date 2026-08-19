using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttendanceChangesAndProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lesson_attendances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    marked_by_teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lesson_attendances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lesson_change_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    proposed_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    proposed_end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lesson_change_requests", x => x.id);
                    table.CheckConstraint("CK_lesson_change_requests_time_range", "proposed_end_at > proposed_start_at");
                });

            migrationBuilder.CreateTable(
                name: "lesson_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practiced = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    homework = table.Column<string>(type: "text", nullable: true),
                    next_goal = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lesson_notes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lesson_rsvps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardian_id = table.Column<Guid>(type: "uuid", nullable: false),
                    response = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lesson_rsvps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "makeup_credits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_lesson_id = table.Column<Guid>(type: "uuid", nullable: false),
                    earned_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    earned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_lesson_id = table.Column<Guid>(type: "uuid", nullable: true),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_makeup_credits", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lesson_attendances_lesson_id",
                table: "lesson_attendances",
                column: "lesson_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lesson_change_requests_lesson_id",
                table: "lesson_change_requests",
                column: "lesson_id");

            migrationBuilder.CreateIndex(
                name: "IX_lesson_change_requests_status",
                table: "lesson_change_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_lesson_notes_lesson_id",
                table: "lesson_notes",
                column: "lesson_id");

            migrationBuilder.CreateIndex(
                name: "IX_lesson_rsvps_lesson_id_guardian_id",
                table: "lesson_rsvps",
                columns: new[] { "lesson_id", "guardian_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_makeup_credits_student_id_status",
                table: "makeup_credits",
                columns: new[] { "student_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lesson_attendances");

            migrationBuilder.DropTable(
                name: "lesson_change_requests");

            migrationBuilder.DropTable(
                name: "lesson_notes");

            migrationBuilder.DropTable(
                name: "lesson_rsvps");

            migrationBuilder.DropTable(
                name: "makeup_credits");
        }
    }
}
