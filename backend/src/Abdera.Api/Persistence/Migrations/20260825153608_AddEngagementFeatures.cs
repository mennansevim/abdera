using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEngagementFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instrument_maintenance_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    maintenance_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    period_days = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    notification_preference = table.Column<string>(type: "text", nullable: false),
                    next_reminder_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instrument_maintenance_settings", x => x.id);
                    table.ForeignKey(
                        name: "FK_instrument_maintenance_settings_instruments_instrument_id",
                        column: x => x.instrument_id,
                        principalTable: "instruments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "practice_journal_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    practice_date = table.Column<DateOnly>(type: "date", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    goal = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    parent_approved_by_guardian_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_practice_journal_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_practice_journal_entries_guardians_parent_approved_by_guard~",
                        column: x => x.parent_approved_by_guardian_id,
                        principalTable: "guardians",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_practice_journal_entries_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "instrument_maintenance_reminders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    setting_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardian_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instrument_maintenance_reminders", x => x.id);
                    table.ForeignKey(
                        name: "FK_instrument_maintenance_reminders_guardians_guardian_id",
                        column: x => x.guardian_id,
                        principalTable: "guardians",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_instrument_maintenance_reminders_instrument_maintenance_set~",
                        column: x => x.setting_id,
                        principalTable: "instrument_maintenance_settings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_instrument_maintenance_reminders_guardian_id",
                table: "instrument_maintenance_reminders",
                column: "guardian_id");

            migrationBuilder.CreateIndex(
                name: "IX_instrument_maintenance_reminders_setting_id_guardian_id_cre~",
                table: "instrument_maintenance_reminders",
                columns: new[] { "setting_id", "guardian_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_instrument_maintenance_settings_instrument_id",
                table: "instrument_maintenance_settings",
                column: "instrument_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_practice_journal_entries_parent_approved_by_guardian_id",
                table: "practice_journal_entries",
                column: "parent_approved_by_guardian_id");

            migrationBuilder.CreateIndex(
                name: "IX_practice_journal_entries_student_id_practice_date",
                table: "practice_journal_entries",
                columns: new[] { "student_id", "practice_date" });

            migrationBuilder.Sql("""
                INSERT INTO message_templates (id, name, language, body, is_active)
                SELECT gen_random_uuid(), 'instrument_maintenance_reminder', 'tr',
                       E'🎻 Enstrüman Bakım Hatırlatması\n\nMerhaba {{guardian_name}},\n\n{{instrument}} için {{maintenance_type}} zamanı geldi. Uygun bir bakım planlamak için okul yönetimiyle iletişime geçebilirsiniz.',
                       true
                WHERE NOT EXISTS (SELECT 1 FROM message_templates WHERE name = 'instrument_maintenance_reminder');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM message_templates WHERE name = 'instrument_maintenance_reminder';");

            migrationBuilder.DropTable(
                name: "instrument_maintenance_reminders");

            migrationBuilder.DropTable(
                name: "practice_journal_entries");

            migrationBuilder.DropTable(
                name: "instrument_maintenance_settings");
        }
    }
}
