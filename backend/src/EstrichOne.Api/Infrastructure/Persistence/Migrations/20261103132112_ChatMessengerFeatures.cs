using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChatMessengerFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "attachments_enabled",
                table: "chat_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "edit_window_minutes",
                table: "chat_settings",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "edited_at",
                table: "chat_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "forwarded",
                table: "chat_messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid[]>(
                name: "mentioned_user_ids",
                table: "chat_messages",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pinned_at",
                table: "chat_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "pinned_by_user_id",
                table: "chat_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "protected_location",
                table: "chat_messages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reply_to_message_id",
                table: "chat_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_admin",
                table: "chat_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "muted_until",
                table: "chat_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "announcement_only",
                table: "chat_conversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "bauakte_id",
                table: "chat_conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "chat_conversations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_key",
                table: "chat_conversations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "image_updated_at",
                table: "chat_conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "chat_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: true),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    protected_name = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    has_thumbnail = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_attachments_chat_conversation_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "chat_conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chat_attachments_chat_message_message_id",
                        column: x => x.message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_reactions",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_reactions", x => new { x.message_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_chat_reactions_chat_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "chat_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chat_user_settings",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    show_preview = table.Column<bool>(type: "boolean", nullable: false),
                    read_receipts = table.Column<bool>(type: "boolean", nullable: false),
                    privacy_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    blocked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    blocked_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_user_settings", x => x.user_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_reply_to_message_id",
                table: "chat_messages",
                column: "reply_to_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_conversations_bauakte_id",
                table: "chat_conversations",
                column: "bauakte_id",
                unique: true,
                filter: "bauakte_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_chat_attachments_conversation_id_created_at",
                table: "chat_attachments",
                columns: new[] { "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_attachments_message_id",
                table: "chat_attachments",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_reactions_user_id",
                table: "chat_reactions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_attachments");

            migrationBuilder.DropTable(
                name: "chat_reactions");

            migrationBuilder.DropTable(
                name: "chat_user_settings");

            migrationBuilder.DropIndex(
                name: "ix_chat_messages_reply_to_message_id",
                table: "chat_messages");

            migrationBuilder.DropIndex(
                name: "ix_chat_conversations_bauakte_id",
                table: "chat_conversations");

            migrationBuilder.DropColumn(
                name: "attachments_enabled",
                table: "chat_settings");

            migrationBuilder.DropColumn(
                name: "edit_window_minutes",
                table: "chat_settings");

            migrationBuilder.DropColumn(
                name: "edited_at",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "forwarded",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "mentioned_user_ids",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "pinned_at",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "pinned_by_user_id",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "protected_location",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "reply_to_message_id",
                table: "chat_messages");

            migrationBuilder.DropColumn(
                name: "is_admin",
                table: "chat_members");

            migrationBuilder.DropColumn(
                name: "muted_until",
                table: "chat_members");

            migrationBuilder.DropColumn(
                name: "announcement_only",
                table: "chat_conversations");

            migrationBuilder.DropColumn(
                name: "bauakte_id",
                table: "chat_conversations");

            migrationBuilder.DropColumn(
                name: "description",
                table: "chat_conversations");

            migrationBuilder.DropColumn(
                name: "image_key",
                table: "chat_conversations");

            migrationBuilder.DropColumn(
                name: "image_updated_at",
                table: "chat_conversations");
        }
    }
}
