using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MediaStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "upload_sha256",
                table: "files",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "upload_size_bytes",
                table: "files",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_files_storage_key",
                table: "files",
                column: "storage_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_files_storage_key",
                table: "files");

            migrationBuilder.DropColumn(
                name: "upload_sha256",
                table: "files");

            migrationBuilder.DropColumn(
                name: "upload_size_bytes",
                table: "files");
        }
    }
}
