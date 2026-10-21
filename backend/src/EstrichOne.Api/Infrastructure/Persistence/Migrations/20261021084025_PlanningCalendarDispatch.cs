using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanningCalendarDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<Guid>>(
                name: "excluded_user_ids",
                table: "assignments",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.CreateTable(
                name: "calendars",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendars", x => x.id);
                    table.ForeignKey(
                        name: "fk_calendars_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    all_day = table.Column<bool>(type: "boolean", nullable: false),
                    start = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    end = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    interval = table.Column<int>(type: "integer", nullable: false),
                    weekdays = table.Column<int>(type: "integer", nullable: false),
                    until = table.Column<DateOnly>(type: "date", nullable: true),
                    count = table.Column<int>(type: "integer", nullable: true),
                    exception_dates = table.Column<List<DateOnly>>(type: "date[]", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    participant_user_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    crew_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_events", x => x.id);
                    table.CheckConstraint("ck_calendar_events_range", "\"end\" > start");
                    table.ForeignKey(
                        name: "fk_calendar_events_bauakten_bauakte_id",
                        column: x => x.bauakte_id,
                        principalTable: "bauakten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_calendar_events_calendars_calendar_id",
                        column: x => x.calendar_id,
                        principalTable: "calendars",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_bauakte_id",
                table: "calendar_events",
                column: "bauakte_id");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_calendar_id_start",
                table: "calendar_events",
                columns: new[] { "calendar_id", "start" });

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_crew_ids",
                table: "calendar_events",
                column: "crew_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_calendar_events_participant_user_ids",
                table: "calendar_events",
                column: "participant_user_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_calendars_owner_user_id",
                table: "calendars",
                column: "owner_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "calendar_events");

            migrationBuilder.DropTable(
                name: "calendars");

            migrationBuilder.DropColumn(
                name: "excluded_user_ids",
                table: "assignments");
        }
    }
}
