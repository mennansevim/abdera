using Abdera.Api.Shared;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Billing.Persistence.Migrations
{
    // Ödeme haftası pazar → cumartesi'den pazartesi → cumartesi'ye geçti (docs/10-decisions.md
    // O1). Uygulama artık week_start'ı pazartesiye normalize ediyor; eski (pazar başlangıçlı)
    // satırlar bir gün kaydırılmazsa aynı haftanın ödemesi "ödenmedi" görünür ve
    // UNIQUE (teacher_id, week_start) çift ödemeyi yakalayamazdı. Yalnızca week_start değişir:
    // week_end (cumartesi), ders sayısı ve tutar ödeme anındaki snapshot olarak kalır.
    // Şema değişmediği için model snapshot'ı da değişmez.
    [DbContext(typeof(AbderaDbContext))]
    [Migration("20260928213000_ShiftTeacherPayWeeksToMonday")]
    public partial class ShiftTeacherPayWeeksToMonday : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE teacher_weekly_payouts SET week_start = week_start + 1 WHERE EXTRACT(DOW FROM week_start) = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE teacher_weekly_payouts SET week_start = week_start - 1 WHERE EXTRACT(DOW FROM week_start) = 1;");
        }
    }
}
