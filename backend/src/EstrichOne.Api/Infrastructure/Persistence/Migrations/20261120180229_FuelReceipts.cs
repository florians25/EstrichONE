using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FuelReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "tank_capacity_liters",
                table: "resources",
                type: "numeric(7,1)",
                precision: 7,
                scale: 1,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fuel_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    vehicle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    odometer_km = table.Column<int>(type: "integer", nullable: true),
                    liters = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    amount_gross = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    fuel_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    station = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    image_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    checked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    settled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fuel_receipts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fuel_receipts_image_file_id",
                table: "fuel_receipts",
                column: "image_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_fuel_receipts_number",
                table: "fuel_receipts",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fuel_receipts_status",
                table: "fuel_receipts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_fuel_receipts_user_id_date",
                table: "fuel_receipts",
                columns: new[] { "user_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_fuel_receipts_vehicle_id_date",
                table: "fuel_receipts",
                columns: new[] { "vehicle_id", "date" });

            // Bestehende Installationen: Standardrollen erhalten die Rechte der Tankbelege wie neue Installationen
            // („Baustelle“ erfasst eigene, „Büro und Bauleitung“ prüft und exportiert).
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (role_id, permission)
                SELECT r.id, p.permission FROM roles r
                CROSS JOIN (VALUES ('fuel.record'), ('fuel.manage'), ('fuel.export')) AS p(permission)
                WHERE r.id = '01a00000-0000-7000-8000-000000000002'
                   OR (r.id = '01a00000-0000-7000-8000-000000000003' AND p.permission = 'fuel.record')
                ON CONFLICT DO NOTHING;
                """);

            // Nummernkreis „Tankbelege“ wie die übrigen Kreise anlegen (Einstellungen › Nummernkreise).
            migrationBuilder.Sql("""
                INSERT INTO number_ranges (record_type, pattern, range_start, range_end, version, created_at, updated_at)
                VALUES ('fuelReceipt', 'TB-{JJJJ}-{NNNN}', 1, NULL, 1, now(), now())
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM role_permissions WHERE permission IN ('fuel.record', 'fuel.manage', 'fuel.export');");
            migrationBuilder.Sql("DELETE FROM number_range_counters WHERE record_type = 'fuelReceipt';");
            migrationBuilder.Sql("DELETE FROM number_ranges WHERE record_type = 'fuelReceipt';");

            migrationBuilder.DropTable(
                name: "fuel_receipts");

            migrationBuilder.DropColumn(
                name: "tank_capacity_liters",
                table: "resources");
        }
    }
}
