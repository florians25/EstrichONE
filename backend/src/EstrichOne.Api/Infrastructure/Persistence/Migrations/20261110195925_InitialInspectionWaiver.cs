using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInspectionWaiver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "initial_inspection_waived",
                table: "bauakten",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "initial_inspection_waived_at",
                table: "bauakten",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "initial_inspection_waived_by_name",
                table: "bauakten",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "initial_inspection_waived_by_user_id",
                table: "bauakten",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "initial_inspection_waived_reason",
                table: "bauakten",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "initial_inspection_waived",
                table: "bauakten");

            migrationBuilder.DropColumn(
                name: "initial_inspection_waived_at",
                table: "bauakten");

            migrationBuilder.DropColumn(
                name: "initial_inspection_waived_by_name",
                table: "bauakten");

            migrationBuilder.DropColumn(
                name: "initial_inspection_waived_by_user_id",
                table: "bauakten");

            migrationBuilder.DropColumn(
                name: "initial_inspection_waived_reason",
                table: "bauakten");
        }
    }
}
