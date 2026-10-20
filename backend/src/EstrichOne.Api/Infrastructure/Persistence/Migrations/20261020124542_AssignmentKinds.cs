using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssignmentKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeOnly>(
                name: "end_time",
                table: "assignments",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "assignments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Screed");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "end_time",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "assignments");
        }
    }
}
