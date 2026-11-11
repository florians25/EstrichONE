using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HelpdeskIntakeChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "channel",
                table: "tickets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "SelfService");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "tickets",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tickets_created_by_user_id",
                table: "tickets",
                column: "created_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_tickets_user_created_by_user_id",
                table: "tickets",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tickets_user_created_by_user_id",
                table: "tickets");

            migrationBuilder.DropIndex(
                name: "ix_tickets_created_by_user_id",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "channel",
                table: "tickets");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "tickets");
        }
    }
}
