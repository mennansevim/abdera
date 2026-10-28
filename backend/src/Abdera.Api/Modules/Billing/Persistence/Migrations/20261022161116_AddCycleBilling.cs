using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCycleBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "accrued_lesson_id",
                table: "receivables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "accrued_on",
                table: "receivables",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "cycle_billing_starts_on",
                table: "billing_settings",
                type: "date",
                nullable: true);

            // docs/10-decisions.md AD: birebir derslerde dönem (4 ders) aidatı 1 Kasım 2026'da başlar.
            // Ekim'in aylık aidatları olduğu gibi kalır (kullanıcı kararı: eski kayıtlara dokunma).
            // Politika satırı yoksa bir şey yazılmaz; tarih Fiyat politikası ekranından seçilir.
            migrationBuilder.Sql("UPDATE billing_settings SET cycle_billing_starts_on = DATE '2026-11-01';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "accrued_lesson_id",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "accrued_on",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "cycle_billing_starts_on",
                table: "billing_settings");
        }
    }
}
