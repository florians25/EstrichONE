using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HrAbsenceRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "probation_end",
                table: "employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "certificate_file_id",
                table: "absences",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "request_id",
                table: "absences",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "absence_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<DateOnly>(type: "date", nullable: false),
                    to = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    absence_id = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_absence_requests", x => x.id);
                    table.CheckConstraint("ck_absence_requests_range", "\"to\" >= \"from\"");
                    table.ForeignKey(
                        name: "fk_absence_requests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    max_crew_absence_percent = table.Column<int>(type: "integer", nullable: false),
                    remind_birthdays = table.Column<bool>(type: "boolean", nullable: false),
                    remind_anniversaries = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_absences_certificate_file_id",
                table: "absences",
                column: "certificate_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_absence_requests_status_from",
                table: "absence_requests",
                columns: new[] { "status", "from" });

            migrationBuilder.CreateIndex(
                name: "ix_absence_requests_user_id",
                table: "absence_requests",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "absence_requests");

            migrationBuilder.DropTable(
                name: "hr_settings");

            migrationBuilder.DropIndex(
                name: "ix_absences_certificate_file_id",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "probation_end",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "certificate_file_id",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "request_id",
                table: "absences");
        }
    }
}
