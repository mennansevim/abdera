using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_corrections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    corrected_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_corrections", x => x.id);
                    table.CheckConstraint("CK_payment_corrections_corrected_amount", "corrected_amount >= 0");
                    table.CheckConstraint("CK_payment_corrections_previous_amount", "previous_amount >= 0");
                    table.ForeignKey(
                        name: "FK_payment_corrections_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_corrections_payment_id_created_at",
                table: "payment_corrections",
                columns: new[] { "payment_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_corrections");
        }
    }
}
