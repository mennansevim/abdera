using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations;

// One-time cleanup for the deterministic demo/bulk-seed dataset. The phone prefix and
// CRUD names are owned by tools/bulk-seed; ordinary school records are never selected.
[DbContext(typeof(AbderaDbContext))]
[Migration("20260912230000_TrimDemoData")]
public sealed class TrimDemoData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TEMP TABLE _demo_bulk_students ON COMMIT DROP AS
            SELECT DISTINCT sg.student_id AS id
            FROM student_guardians sg
            JOIN guardians g ON g.id = sg.guardian_id
            WHERE g.phone_number LIKE '+905003%';

            CREATE TEMP TABLE _demo_bulk_teachers ON COMMIT DROP AS
            SELECT DISTINCT e.teacher_id AS id
            FROM enrollments e
            JOIN _demo_bulk_students s ON s.id = e.student_id;

            CREATE TEMP TABLE _remove_students ON COMMIT DROP AS
            SELECT id
            FROM (
                SELECT s.id, row_number() OVER (ORDER BY s.created_at, s.id) AS position
                FROM students s
                JOIN _demo_bulk_students demo ON demo.id = s.id
            ) ranked
            WHERE position > 150
            UNION
            SELECT id FROM students WHERE first_name = 'CRUDX' AND last_name = 'Ogrenci';

            CREATE TEMP TABLE _remove_teachers ON COMMIT DROP AS
            SELECT id
            FROM (
                SELECT t.id, row_number() OVER (ORDER BY t.created_at, t.id) AS position
                FROM teachers t
                JOIN _demo_bulk_teachers demo ON demo.id = t.id
            ) ranked
            WHERE position > 10
            UNION
            SELECT id FROM teachers WHERE first_name = 'CRUDX' AND last_name = 'Ogretmen' AND user_id IS NULL;

            CREATE TEMP TABLE _demo_price_lists ON COMMIT DROP AS
            SELECT id FROM price_lists
            WHERE name LIKE '%(seed)' OR name LIKE 'Demo %';

            CREATE TEMP TABLE _demo_price_items ON COMMIT DROP AS
            SELECT id FROM price_list_items
            WHERE price_list_id IN (SELECT id FROM _demo_price_lists);

            CREATE TEMP TABLE _demo_billing_enrollments ON COMMIT DROP AS
            SELECT e.id
            FROM enrollments e
            WHERE e.student_id IN (SELECT id FROM _demo_bulk_students);

            CREATE TEMP TABLE _demo_fee_plans ON COMMIT DROP AS
            SELECT id FROM fee_plans
            WHERE enrollment_id IN (SELECT id FROM _demo_billing_enrollments)
               OR price_list_item_id IN (SELECT id FROM _demo_price_items);

            CREATE TEMP TABLE _demo_receivables ON COMMIT DROP AS
            SELECT id FROM receivables
            WHERE enrollment_id IN (SELECT id FROM _demo_billing_enrollments)
               OR price_list_item_id IN (SELECT id FROM _demo_price_items)
               OR fee_plan_id IN (SELECT id FROM _demo_fee_plans);

            CREATE TEMP TABLE _demo_payments ON COMMIT DROP AS
            SELECT id FROM payments
            WHERE receivable_id IN (SELECT id FROM _demo_receivables);

            UPDATE bank_incoming_transactions
            SET matched_receivable_id = NULL
            WHERE matched_receivable_id IN (SELECT id FROM _demo_receivables);
            DELETE FROM payment_corrections WHERE payment_id IN (SELECT id FROM _demo_payments);
            DELETE FROM payments WHERE id IN (SELECT id FROM _demo_payments);
            DELETE FROM receivables WHERE id IN (SELECT id FROM _demo_receivables);
            DELETE FROM fee_plans WHERE id IN (SELECT id FROM _demo_fee_plans);
            DELETE FROM price_list_items WHERE id IN (SELECT id FROM _demo_price_items);
            DELETE FROM price_lists WHERE id IN (SELECT id FROM _demo_price_lists);
            DELETE FROM audit_log WHERE action LIKE 'receivable.%' OR action LIKE 'payment.%';

            CREATE TEMP TABLE _remove_enrollments ON COMMIT DROP AS
            SELECT id FROM enrollments
            WHERE student_id IN (SELECT id FROM _remove_students)
               OR teacher_id IN (SELECT id FROM _remove_teachers);

            CREATE TEMP TABLE _remove_lessons ON COMMIT DROP AS
            SELECT id FROM lessons
            WHERE student_id IN (SELECT id FROM _remove_students)
               OR teacher_id IN (SELECT id FROM _remove_teachers)
               OR lesson_series_id IN (
                   SELECT id FROM lesson_series
                   WHERE enrollment_id IN (SELECT id FROM _remove_enrollments));

            DELETE FROM notification_jobs WHERE reference_id IN (SELECT id FROM _remove_lessons);
            DELETE FROM practice_assignments WHERE lesson_id IN (SELECT id FROM _remove_lessons);
            DELETE FROM skill_assessments
            WHERE lesson_id IN (SELECT id FROM _remove_lessons)
               OR student_id IN (SELECT id FROM _remove_students)
               OR teacher_id IN (SELECT id FROM _remove_teachers);
            DELETE FROM lesson_notes WHERE lesson_id IN (SELECT id FROM _remove_lessons);
            DELETE FROM lesson_attendances WHERE lesson_id IN (SELECT id FROM _remove_lessons);
            DELETE FROM lesson_rsvps WHERE lesson_id IN (SELECT id FROM _remove_lessons);
            DELETE FROM lesson_change_requests WHERE lesson_id IN (SELECT id FROM _remove_lessons);
            DELETE FROM makeup_credits
            WHERE student_id IN (SELECT id FROM _remove_students)
               OR source_lesson_id IN (SELECT id FROM _remove_lessons)
               OR used_lesson_id IN (SELECT id FROM _remove_lessons);
            UPDATE lessons SET original_lesson_id = NULL
            WHERE original_lesson_id IN (SELECT id FROM _remove_lessons)
              AND id NOT IN (SELECT id FROM _remove_lessons);
            DELETE FROM lessons WHERE id IN (SELECT id FROM _remove_lessons);
            DELETE FROM lesson_series WHERE enrollment_id IN (SELECT id FROM _remove_enrollments);
            DELETE FROM audit_log WHERE entity_id IN (SELECT id FROM _remove_enrollments);
            DELETE FROM enrollments WHERE id IN (SELECT id FROM _remove_enrollments);

            DELETE FROM practice_journal_entries WHERE student_id IN (SELECT id FROM _remove_students);
            DELETE FROM student_guardians WHERE student_id IN (SELECT id FROM _remove_students);
            DELETE FROM audit_log WHERE entity_id IN (SELECT id FROM _remove_students);
            DELETE FROM students WHERE id IN (SELECT id FROM _remove_students);

            CREATE TEMP TABLE _remove_guardians ON COMMIT DROP AS
            SELECT g.id
            FROM guardians g
            WHERE (g.phone_number LIKE '+905003%' OR (g.first_name = 'CRUDX' AND g.last_name = 'Veli'))
              AND NOT EXISTS (SELECT 1 FROM student_guardians sg WHERE sg.guardian_id = g.id);

            DELETE FROM guardian_login_codes WHERE guardian_id IN (SELECT id FROM _remove_guardians);
            DELETE FROM instrument_maintenance_reminders WHERE guardian_id IN (SELECT id FROM _remove_guardians);
            DELETE FROM virtual_ibans WHERE guardian_id IN (SELECT id FROM _remove_guardians);
            DELETE FROM whatsapp_messages WHERE guardian_id IN (SELECT id FROM _remove_guardians);
            DELETE FROM audit_log WHERE entity_id IN (SELECT id FROM _remove_guardians);
            DELETE FROM guardians WHERE id IN (SELECT id FROM _remove_guardians);

            DELETE FROM teacher_availability WHERE teacher_id IN (SELECT id FROM _remove_teachers);
            DELETE FROM teacher_time_off WHERE teacher_id IN (SELECT id FROM _remove_teachers);
            DELETE FROM teacher_instruments WHERE teacher_id IN (SELECT id FROM _remove_teachers);
            DELETE FROM audit_log WHERE entity_id IN (SELECT id FROM _remove_teachers);
            DELETE FROM teachers WHERE id IN (SELECT id FROM _remove_teachers);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Destructive cleanup cannot reconstruct discarded demo rows.
    }
}
