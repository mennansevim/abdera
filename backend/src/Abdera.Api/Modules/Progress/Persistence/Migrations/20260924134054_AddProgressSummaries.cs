using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Progress.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProgressSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "progress_summaries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    source_note_count = table.Column<int>(type: "integer", nullable: false),
                    source_latest_note_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_progress_summaries", x => x.id);
                    table.CheckConstraint("ck_progress_summaries_source_note_count", "source_note_count > 0");
                    table.ForeignKey(
                        name: "FK_progress_summaries_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_progress_summaries_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_progress_summaries_student_id_teacher_id",
                table: "progress_summaries",
                columns: new[] { "student_id", "teacher_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_progress_summaries_teacher_id",
                table: "progress_summaries",
                column: "teacher_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "progress_summaries");
        }
    }
}
