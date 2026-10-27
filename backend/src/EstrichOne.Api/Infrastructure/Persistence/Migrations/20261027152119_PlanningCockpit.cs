using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlanningCockpit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sequence",
                table: "assignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "crew_days",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    crew_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    member_user_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    resource_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_crew_days", x => x.id);
                    table.ForeignKey(
                        name: "fk_crew_days_crews_crew_id",
                        column: x => x.crew_id,
                        principalTable: "crews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_releases",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    released_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    released_by_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_releases", x => x.day);
                });

            migrationBuilder.CreateIndex(
                name: "ix_crew_days_crew_id_day",
                table: "crew_days",
                columns: new[] { "crew_id", "day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_crew_days_day",
                table: "crew_days",
                column: "day");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "crew_days");

            migrationBuilder.DropTable(
                name: "plan_releases");

            migrationBuilder.DropColumn(
                name: "sequence",
                table: "assignments");
        }
    }
}
