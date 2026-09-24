using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recurring_expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_expenses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "recurring_expense_amounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recurring_expense_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monthly_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "TRY"),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_until = table.Column<DateOnly>(type: "date", nullable: true),
                    superseded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_expense_amounts", x => x.id);
                    table.CheckConstraint("CK_recurring_expense_amounts_amount", "monthly_amount > 0");
                    table.CheckConstraint("CK_recurring_expense_amounts_range", "effective_until IS NULL OR effective_until >= effective_from");
                    table.ForeignKey(
                        name: "FK_recurring_expense_amounts_recurring_expenses_recurring_expe~",
                        column: x => x.recurring_expense_id,
                        principalTable: "recurring_expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_expense_amounts_one_open",
                table: "recurring_expense_amounts",
                column: "recurring_expense_id",
                unique: true,
                filter: "effective_until IS NULL AND superseded_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_expense_amounts_recurring_expense_id_effective_fr~",
                table: "recurring_expense_amounts",
                columns: new[] { "recurring_expense_id", "effective_from" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_expense_amounts");

            migrationBuilder.DropTable(
                name: "recurring_expenses");
        }
    }
}
