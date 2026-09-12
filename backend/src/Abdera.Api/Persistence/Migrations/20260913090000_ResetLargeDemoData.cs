using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations;

// The previous bulk demo (700 students) was trimmed to 150, but the remaining weekly
// series still made the calendar unusably dense. Remove only the bulk-seed-owned records;
// the normal DevelopmentMockData fixture is recreated by the explicit seed endpoint.
[DbContext(typeof(AbderaDbContext))]
[Migration("20260913090000_ResetLargeDemoData")]
public sealed class ResetLargeDemoData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TEMP TABLE _bulk_students ON COMMIT DROP AS
            SELECT DISTINCT sg.student_id AS id
            FROM student_guardians sg
            JOIN guardians g ON g.id = sg.guardian_id
            WHERE g.phone_number LIKE '+905003%';

            CREATE TEMP TABLE _bulk_teachers ON COMMIT DROP AS
            SELECT DISTINCT e.teacher_id AS id
            FROM enrollments e
            WHERE e.student_id IN (SELECT id FROM _bulk_students);

            CREATE TEMP TABLE _bulk_enrollments ON COMMIT DROP AS
            SELECT id FROM enrollments
            WHERE student_id IN (SELECT id FROM _bulk_students)
               OR teacher_id IN (SELECT id FROM _bulk_teachers);

            CREATE TEMP TABLE _bulk_lessons ON COMMIT DROP AS
            SELECT id FROM lessons
            WHERE student_id IN (SELECT id FROM _bulk_students)
               OR teacher_id IN (SELECT id FROM _bulk_teachers)
               OR lesson_series_id IN (
                   SELECT id FROM lesson_series
                   WHERE enrollment_id IN (SELECT id FROM _bulk_enrollments));

            DELETE FROM notification_jobs WHERE reference_id IN (SELECT id FROM _bulk_lessons);
            DELETE FROM practice_assignments WHERE lesson_id IN (SELECT id FROM _bulk_lessons);
            DELETE FROM skill_assessments
            WHERE lesson_id IN (SELECT id FROM _bulk_lessons)
               OR student_id IN (SELECT id FROM _bulk_students)
               OR teacher_id IN (SELECT id FROM _bulk_teachers);
            DELETE FROM lesson_notes WHERE lesson_id IN (SELECT id FROM _bulk_lessons);
            DELETE FROM lesson_attendances WHERE lesson_id IN (SELECT id FROM _bulk_lessons);
            DELETE FROM lesson_rsvps WHERE lesson_id IN (SELECT id FROM _bulk_lessons);
            DELETE FROM lesson_change_requests WHERE lesson_id IN (SELECT id FROM _bulk_lessons);
            DELETE FROM makeup_credits
            WHERE student_id IN (SELECT id FROM _bulk_students)
               OR source_lesson_id IN (SELECT id FROM _bulk_lessons)
               OR used_lesson_id IN (SELECT id FROM _bulk_lessons);
            UPDATE lessons SET original_lesson_id = NULL
            WHERE original_lesson_id IN (SELECT id FROM _bulk_lessons)
              AND id NOT IN (SELECT id FROM _bulk_lessons);
            DELETE FROM lessons WHERE id IN (SELECT id FROM _bulk_lessons);
            DELETE FROM lesson_series WHERE enrollment_id IN (SELECT id FROM _bulk_enrollments);
            DELETE FROM practice_journal_entries WHERE student_id IN (SELECT id FROM _bulk_students);
            DELETE FROM student_guardians WHERE student_id IN (SELECT id FROM _bulk_students);
            DELETE FROM audit_log
            WHERE entity_id IN (SELECT id FROM _bulk_students)
               OR entity_id IN (SELECT id FROM _bulk_teachers)
               OR entity_id IN (SELECT id FROM _bulk_enrollments);
            DELETE FROM enrollments WHERE id IN (SELECT id FROM _bulk_enrollments);
            DELETE FROM students WHERE id IN (SELECT id FROM _bulk_students);

            CREATE TEMP TABLE _orphan_bulk_guardians ON COMMIT DROP AS
            SELECT g.id FROM guardians g
            WHERE g.phone_number LIKE '+905003%'
              AND NOT EXISTS (SELECT 1 FROM student_guardians sg WHERE sg.guardian_id = g.id);
            DELETE FROM guardian_login_codes WHERE guardian_id IN (SELECT id FROM _orphan_bulk_guardians);
            DELETE FROM instrument_maintenance_reminders WHERE guardian_id IN (SELECT id FROM _orphan_bulk_guardians);
            DELETE FROM virtual_ibans WHERE guardian_id IN (SELECT id FROM _orphan_bulk_guardians);
            DELETE FROM whatsapp_messages WHERE guardian_id IN (SELECT id FROM _orphan_bulk_guardians);
            DELETE FROM audit_log WHERE entity_id IN (SELECT id FROM _orphan_bulk_guardians);
            DELETE FROM guardians WHERE id IN (SELECT id FROM _orphan_bulk_guardians);

            DELETE FROM teacher_availability WHERE teacher_id IN (SELECT id FROM _bulk_teachers);
            DELETE FROM teacher_time_off WHERE teacher_id IN (SELECT id FROM _bulk_teachers);
            DELETE FROM teacher_instruments WHERE teacher_id IN (SELECT id FROM _bulk_teachers);
            DELETE FROM audit_log WHERE entity_id IN (SELECT id FROM _bulk_teachers);
            DELETE FROM teachers WHERE id IN (SELECT id FROM _bulk_teachers);
            DELETE FROM users WHERE email LIKE 'loadtest.teacher%';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Destructive cleanup cannot reconstruct discarded bulk rows.
    }
}
