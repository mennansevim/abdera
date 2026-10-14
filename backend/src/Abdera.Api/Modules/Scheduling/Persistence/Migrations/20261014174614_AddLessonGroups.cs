using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Scheduling.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "lesson_group_id",
                table: "lesson_series",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lesson_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ended_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lesson_groups", x => x.id);
                    table.CheckConstraint("ck_lesson_groups_capacity", "capacity BETWEEN 2 AND 12");
                    table.CheckConstraint("ck_lesson_groups_duration", "duration_minutes BETWEEN 15 AND 180");
                    table.ForeignKey(
                        name: "FK_lesson_groups_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_lesson_groups_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_lesson_series_lesson_group_id",
                table: "lesson_series",
                column: "lesson_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_lesson_groups_instrument_id",
                table: "lesson_groups",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "IX_lesson_groups_teacher_id_status",
                table: "lesson_groups",
                columns: new[] { "teacher_id", "status" });

            migrationBuilder.AddForeignKey(
                name: "FK_lesson_series_lesson_groups_lesson_group_id",
                table: "lesson_series",
                column: "lesson_group_id",
                principalTable: "lesson_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_lesson_series_lesson_groups_lesson_group_id",
                table: "lesson_series");

            migrationBuilder.DropTable(
                name: "lesson_groups");

            migrationBuilder.DropIndex(
                name: "IX_lesson_series_lesson_group_id",
                table: "lesson_series");

            migrationBuilder.DropColumn(
                name: "lesson_group_id",
                table: "lesson_series");
        }
    }
}
