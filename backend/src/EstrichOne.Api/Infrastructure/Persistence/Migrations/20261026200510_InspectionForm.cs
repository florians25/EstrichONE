using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InspectionForm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "check_photos",
                table: "inspections",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "check_remarks",
                table: "inspections",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<long>(
                name: "schema_version",
                table: "inspections",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "values",
                table: "inspections",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "check_photos",
                table: "inspections");

            migrationBuilder.DropColumn(
                name: "check_remarks",
                table: "inspections");

            migrationBuilder.DropColumn(
                name: "schema_version",
                table: "inspections");

            migrationBuilder.DropColumn(
                name: "values",
                table: "inspections");
        }
    }
}
