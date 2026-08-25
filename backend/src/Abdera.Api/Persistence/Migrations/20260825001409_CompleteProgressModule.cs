using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteProgressModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "practice_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    completed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_practice_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_practice_assignments_lessons_lesson_id",
                        column: x => x.lesson_id,
                        principalTable: "lessons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "skill_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skill_definitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_skill_definitions_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "skill_assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skill_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_id = table.Column<Guid>(type: "uuid", nullable: true),
                    score = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    assessed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_skill_assessments", x => x.id);
                    table.CheckConstraint("ck_skill_assessments_score", "score BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_skill_assessments_lessons_lesson_id",
                        column: x => x.lesson_id,
                        principalTable: "lessons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_skill_assessments_skill_definitions_skill_definition_id",
                        column: x => x.skill_definition_id,
                        principalTable: "skill_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_skill_assessments_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_skill_assessments_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_practice_assignments_lesson_id",
                table: "practice_assignments",
                column: "lesson_id");

            migrationBuilder.CreateIndex(
                name: "IX_skill_assessments_lesson_id",
                table: "skill_assessments",
                column: "lesson_id");

            migrationBuilder.CreateIndex(
                name: "IX_skill_assessments_skill_definition_id",
                table: "skill_assessments",
                column: "skill_definition_id");

            migrationBuilder.CreateIndex(
                name: "IX_skill_assessments_student_id_assessed_at",
                table: "skill_assessments",
                columns: new[] { "student_id", "assessed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_skill_assessments_teacher_id",
                table: "skill_assessments",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "IX_skill_definitions_code",
                table: "skill_definitions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_skill_definitions_instrument_id",
                table: "skill_definitions",
                column: "instrument_id");

            migrationBuilder.Sql("""
                INSERT INTO skill_definitions (id, code, label, instrument_id) VALUES
                    (gen_random_uuid(), 'RHYTHM', 'Ritim', NULL),
                    (gen_random_uuid(), 'TEMPO_CONTROL', 'Tempo Kontrolü', NULL),
                    (gen_random_uuid(), 'SIGHT_READING', 'Deşifre', NULL),
                    (gen_random_uuid(), 'MUSICAL_EXPRESSION', 'Müzikal İfade', NULL),
                    (gen_random_uuid(), 'TECHNIQUE', 'Teknik', NULL),
                    (gen_random_uuid(), 'PRACTICE_DISCIPLINE', 'Çalışma Disiplini', NULL);

                INSERT INTO skill_definitions (id, code, label, instrument_id)
                SELECT gen_random_uuid(), seed.code, seed.label, instruments.id
                FROM (VALUES
                    ('PIANO', 'HAND_COORDINATION', 'El Koordinasyonu'),
                    ('PIANO', 'PEDAL_USE', 'Pedal Kullanımı'),
                    ('GUITAR', 'CHORD_TRANSITION', 'Akor Geçişi'),
                    ('GUITAR', 'PICKING', 'Pena Tekniği'),
                    ('GUITAR', 'FINGER_POSITION', 'Parmak Pozisyonu'),
                    ('VIOLIN', 'INTONATION', 'Entonasyon'),
                    ('VIOLIN', 'BOW_CONTROL', 'Yay Kontrolü'),
                    ('VIOLIN', 'LEFT_HAND_POSITION', 'Sol El Pozisyonu'),
                    ('DRUMS', 'TIMING', 'Zamanlama'),
                    ('DRUMS', 'LIMB_INDEPENDENCE', 'Uzuv Bağımsızlığı'),
                    ('DRUMS', 'GROOVE_CONSISTENCY', 'Groove Tutarlılığı')
                ) AS seed(instrument_code, code, label)
                JOIN instruments ON instruments.code = seed.instrument_code;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "practice_assignments");

            migrationBuilder.DropTable(
                name: "skill_assessments");

            migrationBuilder.DropTable(
                name: "skill_definitions");
        }
    }
}
