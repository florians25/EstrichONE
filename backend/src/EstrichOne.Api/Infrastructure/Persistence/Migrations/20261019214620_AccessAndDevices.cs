using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccessAndDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "device_id",
                table: "sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    os_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    app_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", maxLength: 200, nullable: false),
                    assigned_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    registered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_devices_user_approved_by_user_id",
                        column: x => x.approved_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_devices_user_assigned_user_id",
                        column: x => x.assigned_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_devices_user_registered_by_user_id",
                        column: x => x.registered_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_device_id",
                table: "sessions",
                column: "device_id");

            migrationBuilder.CreateIndex(
                name: "ix_devices_approved_by_user_id",
                table: "devices",
                column: "approved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_devices_assigned_user_id",
                table: "devices",
                column: "assigned_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_devices_registered_by_user_id",
                table: "devices",
                column: "registered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_devices_status",
                table: "devices",
                column: "status");

            migrationBuilder.AddForeignKey(
                name: "fk_sessions_devices_device_id",
                table: "sessions",
                column: "device_id",
                principalTable: "devices",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sessions_devices_device_id",
                table: "sessions");

            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropIndex(
                name: "ix_sessions_device_id",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "device_id",
                table: "sessions");
        }
    }
}
