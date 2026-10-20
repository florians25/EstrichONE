using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BauakteDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "files",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cancellation_reason",
                table: "bauakten",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "planned_end",
                table: "bauakten",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "planned_start",
                table: "bauakten",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "site_contact_email",
                table: "bauakten",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "site_contact_name",
                table: "bauakten",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "site_contact_phone",
                table: "bauakten",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "site_notes",
                table: "bauakten",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_bauakten_planned_start",
                table: "bauakten",
                column: "planned_start");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bauakten_planned_start",
                table: "bauakten");

            migrationBuilder.DropColumn(
                name: "category",
                table: "files");

            migrationBuilder.DropColumn(
                name: "cancellation_reason",
                table: "bauakten");

            migrationBuilder.DropColumn(
                name: "planned_end",
                table: "bauakten");

            migrationBuilder.DropColumn(
                name: "planned_start",
                table: "bauakten");

            migrationBuilder.DropColumn(
                name: "site_contact_email",
                table: "bauakten");

            migrationBuilder.DropColumn(
                name: "site_contact_name",
                table: "bauakten");

            migrationBuilder.DropColumn(
                name: "site_contact_phone",
                table: "bauakten");

            migrationBuilder.DropColumn(
                name: "site_notes",
                table: "bauakten");
        }
    }
}
