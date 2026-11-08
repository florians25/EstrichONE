using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Feedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feedback_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    steps_to_reproduce = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    expected_result = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    actual_result = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    problem = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    desired_solution = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    impact = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    context = table.Column<string>(type: "jsonb", nullable: true),
                    attachment_file_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    reporter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    duplicate_of_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_feedback_items_feedback_items_duplicate_of_id",
                        column: x => x.duplicate_of_id,
                        principalTable: "feedback_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_feedback_items_user_assignee_user_id",
                        column: x => x.assignee_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_feedback_items_user_reporter_user_id",
                        column: x => x.reporter_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "feedback_number_sequences",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_number_sequences", x => x.year);
                });

            migrationBuilder.CreateTable(
                name: "feedback_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feedback_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    attachment_file_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_feedback_comments_feedback_item_feedback_id",
                        column: x => x.feedback_id,
                        principalTable: "feedback_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_feedback_comments_user_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "feedback_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feedback_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    from_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    to_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_feedback_events_feedback_item_feedback_id",
                        column: x => x.feedback_id,
                        principalTable: "feedback_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_comments_attachment_file_ids",
                table: "feedback_comments",
                column: "attachment_file_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_comments_author_user_id",
                table: "feedback_comments",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_comments_feedback_id_created_at",
                table: "feedback_comments",
                columns: new[] { "feedback_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_events_feedback_id_at",
                table: "feedback_events",
                columns: new[] { "feedback_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_items_assignee_user_id",
                table: "feedback_items",
                column: "assignee_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_items_attachment_file_ids",
                table: "feedback_items",
                column: "attachment_file_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_items_duplicate_of_id",
                table: "feedback_items",
                column: "duplicate_of_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_items_number",
                table: "feedback_items",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feedback_items_reporter_user_id_created_at",
                table: "feedback_items",
                columns: new[] { "reporter_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_items_status_last_activity_at",
                table: "feedback_items",
                columns: new[] { "status", "last_activity_at" });

            // Bestehende Installationen: Wer Supporttickets bearbeitet (helpdesk.agent), verwaltet auch Meldungen.
            // Neue Installationen erhalten das Recht über die Standardrollen (IT-Administration, Administrator).
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (role_id, permission)
                SELECT role_id, 'feedback.manage' FROM role_permissions WHERE permission = 'helpdesk.agent'
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM role_permissions WHERE permission = 'feedback.manage';");

            migrationBuilder.DropTable(
                name: "feedback_comments");

            migrationBuilder.DropTable(
                name: "feedback_events");

            migrationBuilder.DropTable(
                name: "feedback_number_sequences");

            migrationBuilder.DropTable(
                name: "feedback_items");
        }
    }
}
