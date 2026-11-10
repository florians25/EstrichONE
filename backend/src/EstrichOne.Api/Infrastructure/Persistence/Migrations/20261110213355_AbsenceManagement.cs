using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AbsenceManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "accounting_start_year",
                table: "hr_settings",
                type: "integer",
                nullable: false,
                defaultValue: 2026);

            migrationBuilder.AddColumn<int>(
                name: "carry_over_expiry_day",
                table: "hr_settings",
                type: "integer",
                nullable: false,
                defaultValue: 31);

            migrationBuilder.AddColumn<int>(
                name: "carry_over_expiry_month",
                table: "hr_settings",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<bool>(
                name: "crew_leaders_approve",
                table: "hr_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "prorate_entitlement",
                table: "hr_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "minimum_staff",
                table: "crews",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "closure_id",
                table: "absences",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "half_day_end",
                table: "absences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "half_day_start",
                table: "absences",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "type_id",
                table: "absences",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "half_day_end",
                table: "absence_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "half_day_start",
                table: "absence_requests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "replaces_request_id",
                table: "absence_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "substitute_user_id",
                table: "absence_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "type_id",
                table: "absence_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "absence_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requires_approval = table.Column<bool>(type: "boolean", nullable: false),
                    counts_as_vacation = table.Column<bool>(type: "boolean", nullable: false),
                    requestable = table.Column<bool>(type: "boolean", nullable: false),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_absence_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vacation_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    entitlement = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    carry_over = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    carry_over_expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    adjustment = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vacation_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_vacation_accounts_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_closures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: false),
                    type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_closures", x => x.id);
                    table.CheckConstraint("ck_company_closures_range", "\"to\" >= \"from\"");
                    table.ForeignKey(
                        name: "fk_company_closures_absence_types_type_id",
                        column: x => x.type_id,
                        principalTable: "absence_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "absence_types",
                columns: new[] { "id", "code", "color", "counts_as_vacation", "is_active", "is_paid", "is_system", "key", "kind", "name", "requestable", "requires_approval", "sort_order" },
                values: new object[,]
                {
                    { new Guid("01a10000-0000-7000-8000-000000000001"), "U", "#43A047", true, true, true, true, "vacation", "Vacation", "Urlaub", true, true, 10 },
                    { new Guid("01a10000-0000-7000-8000-000000000002"), "K", "#E53935", false, true, true, true, "sick", "Sick", "Krankheit", false, false, 20 },
                    { new Guid("01a10000-0000-7000-8000-000000000003"), "KK", "#F06292", false, true, false, true, "child_sick", "Sick", "Kind krank", false, false, 30 },
                    { new Guid("01a10000-0000-7000-8000-000000000004"), "B", "#1E88E5", false, true, true, true, "school", "School", "Berufsschule", true, false, 40 },
                    { new Guid("01a10000-0000-7000-8000-000000000005"), "F", "#00ACC1", false, true, true, true, "training", "Training", "Fortbildung", true, true, 50 },
                    { new Guid("01a10000-0000-7000-8000-000000000006"), "SU", "#8E24AA", false, true, true, true, "special_leave", "SpecialLeave", "Sonderurlaub", true, true, 60 },
                    { new Guid("01a10000-0000-7000-8000-000000000007"), "ÜA", "#FB8C00", false, true, true, true, "overtime", "Other", "Überstundenabbau", true, true, 70 },
                    { new Guid("01a10000-0000-7000-8000-000000000008"), "UU", "#6D4C41", false, true, false, true, "unpaid", "Other", "Unbezahlter Urlaub", true, true, 80 },
                    { new Guid("01a10000-0000-7000-8000-000000000009"), "BF", "#7CB342", true, true, true, true, "closure", "Vacation", "Betriebsferien", false, false, 90 },
                    { new Guid("01a10000-0000-7000-8000-00000000000a"), "A", "#9E9E9E", false, true, true, true, "other", "Other", "Sonstige Abwesenheit", true, true, 100 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_absences_closure_id",
                table: "absences",
                column: "closure_id");

            migrationBuilder.CreateIndex(
                name: "ix_absence_types_key",
                table: "absence_types",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_closures_from",
                table: "company_closures",
                column: "from");

            migrationBuilder.CreateIndex(
                name: "ix_company_closures_type_id",
                table: "company_closures",
                column: "type_id");

            migrationBuilder.CreateIndex(
                name: "ix_vacation_accounts_employee_id_year",
                table: "vacation_accounts",
                columns: new[] { "employee_id", "year" },
                unique: true);

            // Übertrag aus dem Personalstamm wird Übertrag im Urlaubskonto des laufenden Jahres; ab diesem Jahr automatische Überträge.
            migrationBuilder.Sql("""
                UPDATE hr_settings SET accounting_start_year = EXTRACT(YEAR FROM (now() AT TIME ZONE 'Europe/Berlin'))::int;
                INSERT INTO vacation_accounts (id, employee_id, year, carry_over, adjustment, version, created_at, updated_at)
                SELECT gen_random_uuid(), id, EXTRACT(YEAR FROM (now() AT TIME ZONE 'Europe/Berlin'))::int, vacation_carry_over, 0, 1, now(), now()
                FROM employees WHERE vacation_carry_over <> 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_closures");

            migrationBuilder.DropTable(
                name: "vacation_accounts");

            migrationBuilder.DropTable(
                name: "absence_types");

            migrationBuilder.DropIndex(
                name: "ix_absences_closure_id",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "accounting_start_year",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "carry_over_expiry_day",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "carry_over_expiry_month",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "crew_leaders_approve",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "prorate_entitlement",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "minimum_staff",
                table: "crews");

            migrationBuilder.DropColumn(
                name: "closure_id",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "half_day_end",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "half_day_start",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "type_id",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "half_day_end",
                table: "absence_requests");

            migrationBuilder.DropColumn(
                name: "half_day_start",
                table: "absence_requests");

            migrationBuilder.DropColumn(
                name: "replaces_request_id",
                table: "absence_requests");

            migrationBuilder.DropColumn(
                name: "substitute_user_id",
                table: "absence_requests");

            migrationBuilder.DropColumn(
                name: "type_id",
                table: "absence_requests");
        }
    }
}
