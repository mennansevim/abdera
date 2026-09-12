using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBulkPaymentMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bulk_payment_id",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bulk_payment_months",
                table: "payments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_bulk_payment_id",
                table: "payments",
                column: "bulk_payment_id");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_bulk_payment_months",
                table: "payments",
                sql: "(bulk_payment_id IS NULL AND bulk_payment_months IS NULL) OR (bulk_payment_id IS NOT NULL AND bulk_payment_months BETWEEN 2 AND 24)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_bulk_payment_id",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_bulk_payment_months",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "bulk_payment_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "bulk_payment_months",
                table: "payments");
        }
    }
}
