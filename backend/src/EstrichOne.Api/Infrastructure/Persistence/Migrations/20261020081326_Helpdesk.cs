using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Helpdesk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ticket_number_sequences",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_number_sequences", x => x.year);
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reporter_blocked = table.Column<bool>(type: "boolean", nullable: false),
                    reporter_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reporter_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bauakte_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    diagnostics = table.Column<string>(type: "jsonb", nullable: true),
                    attachment_file_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tickets", x => x.id);
                    table.ForeignKey(
                        name: "fk_tickets_bauakten_bauakte_id",
                        column: x => x.bauakte_id,
                        principalTable: "bauakten",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_tickets_devices_device_id",
                        column: x => x.device_id,
                        principalTable: "devices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_tickets_user_assignee_user_id",
                        column: x => x.assignee_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tickets_user_reporter_user_id",
                        column: x => x.reporter_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_comments_ticket_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ticket_comments_user_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ticket_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    from_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    to_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_events_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_comments_author_user_id",
                table: "ticket_comments",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_comments_ticket_id_created_at",
                table: "ticket_comments",
                columns: new[] { "ticket_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_events_ticket_id_at",
                table: "ticket_events",
                columns: new[] { "ticket_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_assignee_user_id",
                table: "tickets",
                column: "assignee_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_attachment_file_ids",
                table: "tickets",
                column: "attachment_file_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_bauakte_id",
                table: "tickets",
                column: "bauakte_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_device_id",
                table: "tickets",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_number",
                table: "tickets",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_reporter_user_id_created_at",
                table: "tickets",
                columns: new[] { "reporter_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_status_last_activity_at",
                table: "tickets",
                columns: new[] { "status", "last_activity_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ticket_comments");

            migrationBuilder.DropTable(
                name: "ticket_events");

            migrationBuilder.DropTable(
                name: "ticket_number_sequences");

            migrationBuilder.DropTable(
                name: "tickets");
        }
    }
}
