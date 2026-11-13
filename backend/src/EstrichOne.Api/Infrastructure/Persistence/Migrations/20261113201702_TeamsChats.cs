using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamsChats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "member_sources",
                table: "teams_bauakte_links",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]"); // vorhandene Verknüpfungen: leer = Bauakten-Team

            migrationBuilder.CreateTable(
                name: "teams_crew_chats",
                columns: table => new
                {
                    crew_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    web_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created = table.Column<bool>(type: "boolean", nullable: false),
                    managed_member_ids = table.Column<List<string>>(type: "text[]", nullable: false),
                    linked_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_sync_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_sync_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    synced_members = table.Column<int>(type: "integer", nullable: false),
                    unmapped_members = table.Column<int>(type: "integer", nullable: false),
                    desired_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    sync_pending = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams_crew_chats", x => x.crew_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "teams_crew_chats");

            migrationBuilder.DropColumn(
                name: "member_sources",
                table: "teams_bauakte_links");
        }
    }
}
