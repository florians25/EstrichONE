using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AbsenceWarnings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "certificate_due_day",
                table: "hr_settings",
                type: "integer",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<int>(
                name: "frequent_short_threshold",
                table: "hr_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "notify_crew_leaders",
                table: "hr_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "notify_hr",
                table: "hr_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_planners",
                table: "hr_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "planning_impact_days",
                table: "hr_settings",
                type: "integer",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<bool>(
                name: "warn_adjacent",
                table: "hr_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "warn_bem",
                table: "hr_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "warn_certificate",
                table: "hr_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "warn_continued_pay",
                table: "hr_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "warn_planning_impact",
                table: "hr_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "warn_vacation_overlap",
                table: "hr_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "warnings_enabled",
                table: "hr_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "certificate_retrieved_on",
                table: "absences",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "absence_warning_acks",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_absence_warning_acks", x => x.key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "absence_warning_acks");

            migrationBuilder.DropColumn(
                name: "certificate_due_day",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "frequent_short_threshold",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "notify_crew_leaders",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "notify_hr",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "notify_planners",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "planning_impact_days",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "warn_adjacent",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "warn_bem",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "warn_certificate",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "warn_continued_pay",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "warn_planning_impact",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "warn_vacation_overlap",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "warnings_enabled",
                table: "hr_settings");

            migrationBuilder.DropColumn(
                name: "certificate_retrieved_on",
                table: "absences");
        }
    }
}
