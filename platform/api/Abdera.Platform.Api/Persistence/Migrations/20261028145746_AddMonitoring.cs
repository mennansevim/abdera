using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Abdera.Platform.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "db_connections",
                table: "telemetry_snapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "db_waiting_on_locks",
                table: "telemetry_snapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "errors24h",
                table: "telemetry_snapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_error_at",
                table: "telemetry_snapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_migration",
                table: "telemetry_snapshots",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "warnings24h",
                table: "telemetry_snapshots",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "host_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reachable = table.Column<bool>(type: "boolean", nullable: false),
                    load1 = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    load5 = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    cpus = table.Column<int>(type: "integer", nullable: true),
                    mem_total_mb = table.Column<int>(type: "integer", nullable: true),
                    mem_available_mb = table.Column<int>(type: "integer", nullable: true),
                    disk_total_mb = table.Column<int>(type: "integer", nullable: true),
                    disk_free_mb = table.Column<int>(type: "integer", nullable: true),
                    uptime_seconds = table.Column<long>(type: "bigint", nullable: true),
                    containers_json = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_host_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_host_snapshots_host_id_taken_at",
                table: "host_snapshots",
                columns: new[] { "host_id", "taken_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "host_snapshots");

            migrationBuilder.DropColumn(
                name: "db_connections",
                table: "telemetry_snapshots");

            migrationBuilder.DropColumn(
                name: "db_waiting_on_locks",
                table: "telemetry_snapshots");

            migrationBuilder.DropColumn(
                name: "errors24h",
                table: "telemetry_snapshots");

            migrationBuilder.DropColumn(
                name: "last_error_at",
                table: "telemetry_snapshots");

            migrationBuilder.DropColumn(
                name: "last_migration",
                table: "telemetry_snapshots");

            migrationBuilder.DropColumn(
                name: "warnings24h",
                table: "telemetry_snapshots");
        }
    }
}
