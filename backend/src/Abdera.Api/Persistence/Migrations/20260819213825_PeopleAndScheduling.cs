using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PeopleAndScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guardians",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    whatsapp_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    notification_consent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    consent_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    conversation_window_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guardians", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "instruments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instruments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lesson_series",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_until = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lesson_series", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lessons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    original_lesson_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_lessons", x => x.id);
                    table.CheckConstraint("CK_lessons_time_range", "end_at > start_at");
                });

            migrationBuilder.CreateTable(
                name: "school_calendar_days",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_school_calendar_days", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "students",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_students", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teacher_availability",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teacher_availability", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teacher_time_off",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teacher_time_off", x => x.id);
                    table.CheckConstraint("CK_teacher_time_off_dates", "ends_on >= starts_on");
                });

            migrationBuilder.CreateTable(
                name: "teachers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teachers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "student_guardians",
                columns: table => new
                {
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardian_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_student_guardians", x => new { x.student_id, x.guardian_id });
                    table.ForeignKey(
                        name: "FK_student_guardians_guardians_guardian_id",
                        column: x => x.guardian_id,
                        principalTable: "guardians",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_student_guardians_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateOnly>(type: "date", nullable: false),
                    ended_at = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enrollments", x => x.id);
                    table.ForeignKey(
                        name: "FK_enrollments_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollments_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollments_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teacher_instruments",
                columns: table => new
                {
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teacher_instruments", x => new { x.teacher_id, x.instrument_id });
                    table.ForeignKey(
                        name: "FK_teacher_instruments_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_teacher_instruments_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_instrument_id",
                table: "enrollments",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_student_id",
                table: "enrollments",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_teacher_id",
                table: "enrollments",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "IX_guardians_phone_number",
                table: "guardians",
                column: "phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_instruments_code",
                table: "instruments",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_instruments_name",
                table: "instruments",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lesson_series_enrollment_id",
                table: "lesson_series",
                column: "enrollment_id");

            migrationBuilder.CreateIndex(
                name: "IX_lessons_lesson_series_id_start_at",
                table: "lessons",
                columns: new[] { "lesson_series_id", "start_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_lessons_start_at",
                table: "lessons",
                column: "start_at");

            migrationBuilder.CreateIndex(
                name: "IX_lessons_student_id",
                table: "lessons",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "IX_lessons_teacher_id",
                table: "lessons",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "IX_school_calendar_days_date",
                table: "school_calendar_days",
                column: "date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_student_guardians_guardian_id",
                table: "student_guardians",
                column: "guardian_id");

            migrationBuilder.CreateIndex(
                name: "IX_students_last_name_first_name",
                table: "students",
                columns: new[] { "last_name", "first_name" });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_availability_teacher_id_day_of_week",
                table: "teacher_availability",
                columns: new[] { "teacher_id", "day_of_week" });

            migrationBuilder.CreateIndex(
                name: "IX_teacher_instruments_instrument_id",
                table: "teacher_instruments",
                column: "instrument_id");

            migrationBuilder.CreateIndex(
                name: "IX_teacher_time_off_teacher_id_starts_on_ends_on",
                table: "teacher_time_off",
                columns: new[] { "teacher_id", "starts_on", "ends_on" });

            migrationBuilder.CreateIndex(
                name: "IX_teachers_user_id",
                table: "teachers",
                column: "user_id",
                unique: true,
                filter: "user_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enrollments");

            migrationBuilder.DropTable(
                name: "lesson_series");

            migrationBuilder.DropTable(
                name: "lessons");

            migrationBuilder.DropTable(
                name: "school_calendar_days");

            migrationBuilder.DropTable(
                name: "student_guardians");

            migrationBuilder.DropTable(
                name: "teacher_availability");

            migrationBuilder.DropTable(
                name: "teacher_instruments");

            migrationBuilder.DropTable(
                name: "teacher_time_off");

            migrationBuilder.DropTable(
                name: "guardians");

            migrationBuilder.DropTable(
                name: "students");

            migrationBuilder.DropTable(
                name: "instruments");

            migrationBuilder.DropTable(
                name: "teachers");
        }
    }
}
