using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResourceDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "crew_id",
                table: "resources",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "document_file_ids",
                table: "resources",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<Guid>(
                name: "image_file_id",
                table: "resources",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "inspection_due",
                table: "resources",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "license_class",
                table: "resources",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "model",
                table: "resources",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "service_due",
                table: "resources",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_resources_crew_id",
                table: "resources",
                column: "crew_id");

            migrationBuilder.AddForeignKey(
                name: "fk_resources_crews_crew_id",
                table: "resources",
                column: "crew_id",
                principalTable: "crews",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_resources_crews_crew_id",
                table: "resources");

            migrationBuilder.DropIndex(
                name: "ix_resources_crew_id",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "crew_id",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "document_file_ids",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "image_file_id",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "inspection_due",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "license_class",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "model",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "service_due",
                table: "resources");
        }
    }
}
