using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseNoteReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "release_note_receipts",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_release_note_receipts", x => new { x.user_id, x.channel });
                });

            // Bestehende Benutzer haben 1.0.26 gesehen: nach dem Update erscheint einmal „Was gibt's Neues“ für die neue
            // Version. Benutzer ohne Zeile (neu angelegt, Neuinstallation) werden beim ersten Abruf still eingetragen.
            migrationBuilder.Sql("""
                INSERT INTO release_note_receipts (user_id, channel, version, seen_at)
                SELECT u.id, c.channel, '1.0.26', now() FROM users u CROSS JOIN (VALUES ('web'), ('apps')) AS c(channel)
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "release_note_receipts");
        }
    }
}
