using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.People.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExplicitSiblingDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "sibling_discount",
                table: "students",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // GERİYE DÖNÜK DOLDURMA - bu migration'ın asıl işi.
            //
            // Kardeş indirimi bugüne kadar çıkarımdı: aynı veliye bağlı, AKTİF kursu olan
            // 2+ öğrenci otomatik kardeş sayılıyordu (eski TuitionPricer.LoadAsync). Kutuyu
            // herkes için boş bırakırsak bugün indirim alan öğrenciler bir sonraki aidat
            // üretiminde sessizce %5 zam görürdü. Bu yüzden kutu, eski kuralın BUGÜN kimi
            // kardeş saydığıyla doldurulur: kimsenin tutarı deploy anında değişmez.
            //
            // Bu noktadan sonra çıkarım çalışmaz; kutu yöneticinin elindedir (H13).
            migrationBuilder.Sql("""
                UPDATE students
                SET sibling_discount = true
                WHERE id IN (
                    SELECT link.student_id
                    FROM student_guardians link
                    WHERE EXISTS (
                            SELECT 1 FROM enrollments e
                            WHERE e.student_id = link.student_id AND e.status = 'Active')
                      AND EXISTS (
                            SELECT 1
                            FROM student_guardians other
                            WHERE other.guardian_id = link.guardian_id
                              AND other.student_id <> link.student_id
                              AND EXISTS (
                                    SELECT 1 FROM enrollments e2
                                    WHERE e2.student_id = other.student_id AND e2.status = 'Active'))
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sibling_discount",
                table: "students");
        }
    }
}
