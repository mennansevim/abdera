using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Scheduling.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WidenTeacherAvailabilityTo2100 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Okulun çalışma penceresi 09:00-21:00 oldu. "Uygun günler" arayüzü bir günü açarken
            // sabit 09:00-19:00 yazıyordu; bu satırlar 19:00'dan sonra biten her ders serisini
            // EnsureWithinAvailabilityAsync'te reddettiriyordu. Yalnızca o eski varsayılanla
            // birebir eşleşen satırlar genişletilir - elle/seed ile girilmiş başka pencerelere
            // dokunulmaz.
            migrationBuilder.Sql("""
                UPDATE teacher_availability
                SET end_time = TIME '21:00'
                WHERE start_time = TIME '09:00' AND end_time = TIME '19:00';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE teacher_availability
                SET end_time = TIME '19:00'
                WHERE start_time = TIME '09:00' AND end_time = TIME '21:00';
                """);
        }
    }
}
