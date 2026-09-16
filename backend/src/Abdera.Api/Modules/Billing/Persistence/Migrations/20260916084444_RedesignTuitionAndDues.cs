using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Api.Modules.Billing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RedesignTuitionAndDues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fee_plans");

            migrationBuilder.DropTable(
                name: "price_list_items");

            migrationBuilder.DropTable(
                name: "price_lists");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_bulk_payment_months",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "fee_plan_id",
                table: "receivables");

            migrationBuilder.RenameColumn(
                name: "price_list_item_id",
                table: "receivables",
                newName: "tuition_rate_id");

            migrationBuilder.RenameColumn(
                name: "bulk_payment_months",
                table: "payments",
                newName: "prepay_plan_months");

            migrationBuilder.RenameColumn(
                name: "bulk_payment_id",
                table: "payments",
                newName: "prepay_plan_id");

            migrationBuilder.RenameIndex(
                name: "IX_payments_bulk_payment_id",
                table: "payments",
                newName: "IX_payments_prepay_plan_id");

            migrationBuilder.AddColumn<decimal>(
                name: "base_amount",
                table: "receivables",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_percent",
                table: "receivables",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "discount_reason",
                table: "receivables",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "prepay_plan_id",
                table: "receivables",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "course_kind",
                table: "enrollments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Individual");

            migrationBuilder.AddColumn<decimal>(
                name: "manual_discount_percent",
                table: "enrollments",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "manual_discount_reason",
                table: "enrollments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "billing_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    multi_course_discount_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    sibling_discount_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    due_day_of_month = table.Column<int>(type: "integer", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_billing_settings", x => x.id);
                    table.CheckConstraint("CK_billing_settings_due_day", "due_day_of_month BETWEEN 1 AND 28");
                });

            migrationBuilder.CreateTable(
                name: "prepay_discount_tiers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_months = table.Column<int>(type: "integer", nullable: false),
                    percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prepay_discount_tiers", x => x.id);
                    table.CheckConstraint("CK_prepay_tiers_months", "min_months BETWEEN 2 AND 24");
                    table.CheckConstraint("CK_prepay_tiers_percent", "percent >= 0 AND percent <= 100");
                });

            migrationBuilder.CreateTable(
                name: "tuition_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    lessons_per_month = table.Column<int>(type: "integer", nullable: false),
                    monthly_amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "TRY"),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_until = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tuition_rates", x => x.id);
                    table.CheckConstraint("CK_tuition_rates_amount", "monthly_amount >= 0");
                    table.CheckConstraint("CK_tuition_rates_effective_range", "effective_until IS NULL OR effective_until >= effective_from");
                });

            migrationBuilder.CreateIndex(
                name: "IX_receivables_prepay_plan_id",
                table: "receivables",
                column: "prepay_plan_id",
                filter: "prepay_plan_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_receivables_base_amount",
                table: "receivables",
                sql: "base_amount >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_receivables_discount_percent",
                table: "receivables",
                sql: "discount_percent >= 0 AND discount_percent <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_prepay_plan_months",
                table: "payments",
                sql: "(prepay_plan_id IS NULL AND prepay_plan_months IS NULL) OR (prepay_plan_id IS NOT NULL AND prepay_plan_months BETWEEN 2 AND 24)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_enrollments_manual_discount_percent",
                table: "enrollments",
                sql: "manual_discount_percent IS NULL OR (manual_discount_percent >= 0 AND manual_discount_percent <= 100)");

            migrationBuilder.CreateIndex(
                name: "IX_prepay_discount_tiers_min_months",
                table: "prepay_discount_tiers",
                column: "min_months",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tuition_rates_course_kind_effective_from",
                table: "tuition_rates",
                columns: new[] { "course_kind", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_tuition_rates_one_open_per_kind",
                table: "tuition_rates",
                column: "course_kind",
                unique: true,
                filter: "effective_until IS NULL");

            // --- Başlangıç verisi ---------------------------------------------------
            // Yeni model, tanımlı bir tarife olmadan aidat üretmez (sessizce 0 TL'lik satır
            // açmaz). Bu yüzden okulun Eylül 2026'da velilere duyurduğu ücretler ve indirim
            // politikası buradan seed edilir - aksi hâlde göç sonrası ilk açılışta aidat
            // ekranı yine boş kalırdı.
            migrationBuilder.Sql("""
                -- Resim grup dersi olarak veriliyor ama enstrüman listesinde hiç yoktu.
                INSERT INTO instruments (id, name, code)
                SELECT gen_random_uuid(), 'Resim', 'ART'
                WHERE NOT EXISTS (SELECT 1 FROM instruments WHERE code = 'ART');

                -- "2 kursa katılanlar & kardeşler için %5", vade ayın 1'i.
                INSERT INTO billing_settings (
                    id, multi_course_discount_percent, sibling_discount_percent, due_day_of_month, updated_at)
                VALUES ('00000000-0000-0000-0000-0000000000b1', 5, 5, 1, now())
                ON CONFLICT (id) DO NOTHING;

                -- Eylül 2026 tarifesi: Birebir 4 ders 6.000 TL, Grup 4 ders 4.500 TL.
                INSERT INTO tuition_rates (
                    id, course_kind, lessons_per_month, monthly_amount, currency, effective_from, created_at)
                SELECT gen_random_uuid(), 'Individual', 4, 6000.00, 'TRY', DATE '2026-09-01', now()
                WHERE NOT EXISTS (SELECT 1 FROM tuition_rates WHERE course_kind = 'Individual');

                INSERT INTO tuition_rates (
                    id, course_kind, lessons_per_month, monthly_amount, currency, effective_from, created_at)
                SELECT gen_random_uuid(), 'Group', 4, 4500.00, 'TRY', DATE '2026-09-01', now()
                WHERE NOT EXISTS (SELECT 1 FROM tuition_rates WHERE course_kind = 'Group');

                -- Toplu ödeme kampanyası kademeleri: 4+ ay %5, 10+ ay (tüm sezon) %10.
                INSERT INTO prepay_discount_tiers (id, min_months, percent)
                SELECT gen_random_uuid(), 4, 5
                WHERE NOT EXISTS (SELECT 1 FROM prepay_discount_tiers WHERE min_months = 4);

                INSERT INTO prepay_discount_tiers (id, min_months, percent)
                SELECT gen_random_uuid(), 10, 10
                WHERE NOT EXISTS (SELECT 1 FROM prepay_discount_tiers WHERE min_months = 10);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM instruments
                WHERE code = 'ART'
                  AND NOT EXISTS (SELECT 1 FROM enrollments e WHERE e.instrument_id = instruments.id);
                """);

            migrationBuilder.DropTable(
                name: "billing_settings");

            migrationBuilder.DropTable(
                name: "prepay_discount_tiers");

            migrationBuilder.DropTable(
                name: "tuition_rates");

            migrationBuilder.DropIndex(
                name: "IX_receivables_prepay_plan_id",
                table: "receivables");

            migrationBuilder.DropCheckConstraint(
                name: "CK_receivables_base_amount",
                table: "receivables");

            migrationBuilder.DropCheckConstraint(
                name: "CK_receivables_discount_percent",
                table: "receivables");

            migrationBuilder.DropCheckConstraint(
                name: "CK_payments_prepay_plan_months",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_enrollments_manual_discount_percent",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "base_amount",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "discount_percent",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "discount_reason",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "prepay_plan_id",
                table: "receivables");

            migrationBuilder.DropColumn(
                name: "course_kind",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "manual_discount_percent",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "manual_discount_reason",
                table: "enrollments");

            migrationBuilder.RenameColumn(
                name: "tuition_rate_id",
                table: "receivables",
                newName: "price_list_item_id");

            migrationBuilder.RenameColumn(
                name: "prepay_plan_months",
                table: "payments",
                newName: "bulk_payment_months");

            migrationBuilder.RenameColumn(
                name: "prepay_plan_id",
                table: "payments",
                newName: "bulk_payment_id");

            migrationBuilder.RenameIndex(
                name: "IX_payments_prepay_plan_id",
                table: "payments",
                newName: "IX_payments_bulk_payment_id");

            migrationBuilder.AddColumn<Guid>(
                name: "fee_plan_id",
                table: "receivables",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "fee_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    active_from = table.Column<DateOnly>(type: "date", nullable: false),
                    active_until = table.Column<DateOnly>(type: "date", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    billing_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "TRY"),
                    due_day = table.Column<int>(type: "integer", nullable: true),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_lesson_count = table.Column<int>(type: "integer", nullable: true),
                    price_list_item_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fee_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_lists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_until = table.Column<DateOnly>(type: "date", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_lists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_list_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    billing_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "TRY"),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    instrument_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_lesson_count = table.Column<int>(type: "integer", nullable: true),
                    price_list_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_list_items", x => x.id);
                    table.CheckConstraint("CK_price_list_items_amount", "amount >= 0");
                    table.ForeignKey(
                        name: "FK_price_list_items_price_lists_price_list_id",
                        column: x => x.price_list_id,
                        principalTable: "price_lists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_payments_bulk_payment_months",
                table: "payments",
                sql: "(bulk_payment_id IS NULL AND bulk_payment_months IS NULL) OR (bulk_payment_id IS NOT NULL AND bulk_payment_months BETWEEN 2 AND 24)");

            migrationBuilder.CreateIndex(
                name: "IX_fee_plans_enrollment_id",
                table: "fee_plans",
                column: "enrollment_id");

            migrationBuilder.CreateIndex(
                name: "IX_price_list_items_instrument_id_duration_minutes_billing_type",
                table: "price_list_items",
                columns: new[] { "instrument_id", "duration_minutes", "billing_type" });

            migrationBuilder.CreateIndex(
                name: "IX_price_list_items_price_list_id",
                table: "price_list_items",
                column: "price_list_id");
        }
    }
}
