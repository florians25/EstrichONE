using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LexwareAutoCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auto_create",
                table: "lexware_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "lexware_contact_creations",
                columns: table => new
                {
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lexware_contact_creations", x => x.contact_id);
                });
        
            // Bestandskunden mit eingeschaltetem Abgleich und ohne Verknüpfung werden nicht ungefragt in Lexware Office angelegt.
            migrationBuilder.Sql("""
                INSERT INTO lexware_contact_creations (contact_id, state, attempts, updated_at)
                SELECT c.id, 'skipped', 0, now() FROM contacts c
                WHERE c.lexware_sync AND NOT EXISTS (SELECT 1 FROM lexware_contact_links l WHERE l.contact_id = c.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lexware_contact_creations");

            migrationBuilder.DropColumn(
                name: "auto_create",
                table: "lexware_settings");
        }
    }
}
