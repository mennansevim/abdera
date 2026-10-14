using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherMonthlyGroupPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "teacher_monthly_group_payouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    revenue_base = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    receivable_count = table.Column<int>(type: "integer", nullable: false),
                    share_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    computed_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "TRY"),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    expense_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teacher_monthly_group_payouts", x => x.id);
                    table.CheckConstraint("CK_teacher_monthly_group_payouts_amount", "amount > 0");
                    table.CheckConstraint("CK_teacher_monthly_group_payouts_revenue", "revenue_base > 0");
                    table.CheckConstraint("CK_teacher_monthly_group_payouts_share", "share_percent > 0 AND share_percent <= 100");
                });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_monthly_group_payouts_teacher_period",
                table: "teacher_monthly_group_payouts",
                columns: new[] { "teacher_id", "period" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "teacher_monthly_group_payouts");
        }
    }
}
