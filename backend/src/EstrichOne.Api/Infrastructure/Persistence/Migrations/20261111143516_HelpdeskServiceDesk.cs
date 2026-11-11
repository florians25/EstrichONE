using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HelpdeskServiceDesk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tickets_assignee_user_id",
                table: "tickets");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "article_ids",
                table: "tickets",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "first_response_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_ticket_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "overdue_notified_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rated_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "rating",
                table: "tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rating_comment",
                table: "tickets",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resolution_due_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "response_due_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sla_paused_at",
                table: "tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sla_paused_minutes",
                table: "tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "subcategory_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "watcher_user_ids",
                table: "tickets",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<List<Guid>>(
                name: "mention_user_ids",
                table: "ticket_comments",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.CreateTable(
                name: "helpdesk_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    member_user_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false, defaultValueSql: "'{}'::uuid[]"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_helpdesk_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "helpdesk_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_helpdesk_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_articles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    keywords = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    views = table.Column<int>(type: "integer", nullable: false),
                    helpful_count = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_articles", x => x.id);
                    table.ForeignKey(
                        name: "fk_knowledge_articles_user_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_knowledge_articles_user_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ticket_canned_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_canned_responses", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_canned_responses_user_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    other_ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_links_tickets_other_ticket_id",
                        column: x => x.other_ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ticket_links_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_subcategories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_subcategories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_time_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ticket_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    minutes = table.Column<int>(type: "integer", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_time_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_time_entries_tickets_ticket_id",
                        column: x => x.ticket_id,
                        principalTable: "tickets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ticket_time_entries_user_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "helpdesk_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    subcategory_id = table.Column<Guid>(type: "uuid", nullable: true),
                    keyword = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    strategy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assignee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_helpdesk_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_helpdesk_rules_helpdesk_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "helpdesk_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_helpdesk_rules_ticket_subcategory_subcategory_id",
                        column: x => x.subcategory_id,
                        principalTable: "ticket_subcategories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_helpdesk_rules_user_assignee_user_id",
                        column: x => x.assignee_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_assignee_user_id_status",
                table: "tickets",
                columns: new[] { "assignee_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_group_id",
                table: "tickets",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_merged_into_ticket_id",
                table: "tickets",
                column: "merged_into_ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_subcategory_id",
                table: "tickets",
                column: "subcategory_id");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_watcher_user_ids",
                table: "tickets",
                column: "watcher_user_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_helpdesk_rules_assignee_user_id",
                table: "helpdesk_rules",
                column: "assignee_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_helpdesk_rules_group_id",
                table: "helpdesk_rules",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_helpdesk_rules_subcategory_id",
                table: "helpdesk_rules",
                column: "subcategory_id");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_articles_author_user_id",
                table: "knowledge_articles",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_articles_updated_by_user_id",
                table: "knowledge_articles",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_canned_responses_owner_user_id",
                table: "ticket_canned_responses",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_links_other_ticket_id",
                table: "ticket_links",
                column: "other_ticket_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_links_ticket_id_other_ticket_id",
                table: "ticket_links",
                columns: new[] { "ticket_id", "other_ticket_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ticket_subcategories_category_sort_order",
                table: "ticket_subcategories",
                columns: new[] { "category", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_time_entries_ticket_id_work_date",
                table: "ticket_time_entries",
                columns: new[] { "ticket_id", "work_date" });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_time_entries_user_id",
                table: "ticket_time_entries",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_helpdesk_groups_group_id",
                table: "tickets",
                column: "group_id",
                principalTable: "helpdesk_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_ticket_subcategory_subcategory_id",
                table: "tickets",
                column: "subcategory_id",
                principalTable: "ticket_subcategories",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_tickets_merged_into_ticket_id",
                table: "tickets",
                column: "merged_into_ticket_id",
                principalTable: "tickets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // IT-Administration (Standardrolle) erhält das neue Recht zum Einrichten des Helpdesks, sofern sie Tickets bearbeitet.
            migrationBuilder.Sql("""
                INSERT INTO role_permissions (role_id, permission)
                SELECT role_id, 'helpdesk.manage' FROM role_permissions
                WHERE permission = 'helpdesk.agent' AND role_id = '01a00000-0000-7000-8000-000000000004'
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tickets_helpdesk_groups_group_id",
                table: "tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_tickets_ticket_subcategory_subcategory_id",
                table: "tickets");

            migrationBuilder.DropForeignKey(
                name: "fk_tickets_tickets_merged_into_ticket_id",
                table: "tickets");

            migrationBuilder.DropTable(
                name: "helpdesk_rules");

            migrationBuilder.DropTable(
                name: "helpdesk_settings");

            migrationBuilder.DropTable(
                name: "knowledge_articles");

            migrationBuilder.DropTable(
                name: "ticket_canned_responses");

            migrationBuilder.DropTable(
                name: "ticket_links");

            migrationBuilder.DropTable(
                name: "ticket_time_entries");

            migrationBuilder.DropTable(
                name: "helpdesk_groups");

            migrationBuilder.DropTable(
                name: "ticket_subcategories");

            migrationBuilder.DropIndex(
                name: "ix_tickets_assignee_user_id_status",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_group_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_merged_into_ticket_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_subcategory_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_watcher_user_ids",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "article_ids",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "first_response_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "merged_into_ticket_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "overdue_notified_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "rated_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "rating",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "rating_comment",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "resolution_due_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "response_due_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_paused_at",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "sla_paused_minutes",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "subcategory_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "watcher_user_ids",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "mention_user_ids",
                table: "ticket_comments");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_assignee_user_id",
                table: "tickets",
                column: "assignee_user_id");
        }
    }
}
