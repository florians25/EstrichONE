using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DocumentationOffice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "folder",
                table: "files",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "previous_version_id",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "room_id",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "work_item_id",
                table: "files",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "crew",
                table: "diary_entries",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "end_time",
                table: "diary_entries",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.AddColumn<List<Guid>>(
                name: "photo_file_ids",
                table: "diary_entries",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "start_time",
                table: "diary_entries",
                type: "time without time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_files_previous_version_id",
                table: "files",
                column: "previous_version_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_files_previous_version_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "folder",
                table: "files");

            migrationBuilder.DropColumn(
                name: "previous_version_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "room_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "work_item_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "crew",
                table: "diary_entries");

            migrationBuilder.DropColumn(
                name: "end_time",
                table: "diary_entries");

            migrationBuilder.DropColumn(
                name: "photo_file_ids",
                table: "diary_entries");

            migrationBuilder.DropColumn(
                name: "start_time",
                table: "diary_entries");
        }
    }
}
