using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Progress.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioClips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "audio_consent_updated_at",
                table: "students",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "audio_recording_consent",
                table: "students",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "audio_clips",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lesson_id = table.Column<Guid>(type: "uuid", nullable: true),
                    piece_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    file_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    starred = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    shared_with_guardian_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    shared_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    backed_up_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audio_clips", x => x.id);
                    table.CheckConstraint("ck_audio_clips_duration", "duration_seconds BETWEEN 1 AND 60");
                    table.CheckConstraint("ck_audio_clips_size", "size_bytes > 0 AND size_bytes <= 409600");
                    table.ForeignKey(
                        name: "FK_audio_clips_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audio_clips_file_name",
                table: "audio_clips",
                column: "file_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audio_clips_lesson_id",
                table: "audio_clips",
                column: "lesson_id");

            migrationBuilder.CreateIndex(
                name: "ix_audio_clips_not_backed_up",
                table: "audio_clips",
                column: "created_at",
                filter: "backed_up_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_audio_clips_student_id_created_at",
                table: "audio_clips",
                columns: new[] { "student_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audio_clips");

            migrationBuilder.DropColumn(
                name: "audio_consent_updated_at",
                table: "students");

            migrationBuilder.DropColumn(
                name: "audio_recording_consent",
                table: "students");
        }
    }
}
