using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Hosting.Data.Migrations
{
    /// <inheritdoc />
    public partial class Rollen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Admins",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Administrator");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "Admins");
        }
    }
}
