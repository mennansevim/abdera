using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveEnrollmentUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_enrollments_student_id_teacher_id_instrument_id",
                table: "enrollments",
                columns: new[] { "student_id", "teacher_id", "instrument_id" },
                unique: true,
                filter: "status = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_enrollments_student_id_teacher_id_instrument_id",
                table: "enrollments");
        }
    }
}
