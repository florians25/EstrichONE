using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BauaktePartyDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "bauakte_parties",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_primary",
                table: "bauakte_parties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "mobile",
                table: "bauakte_parties",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "postal_code",
                table: "bauakte_parties",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "street",
                table: "bauakte_parties",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "city",
                table: "bauakte_parties");

            migrationBuilder.DropColumn(
                name: "is_primary",
                table: "bauakte_parties");

            migrationBuilder.DropColumn(
                name: "mobile",
                table: "bauakte_parties");

            migrationBuilder.DropColumn(
                name: "postal_code",
                table: "bauakte_parties");

            migrationBuilder.DropColumn(
                name: "street",
                table: "bauakte_parties");
        }
    }
}
