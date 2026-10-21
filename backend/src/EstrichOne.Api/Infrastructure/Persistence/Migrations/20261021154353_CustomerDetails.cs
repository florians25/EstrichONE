using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "billing_address",
                table: "contacts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "birthday",
                table: "contacts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_address",
                table: "contacts",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "emails",
                table: "contacts",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "phones",
                table: "contacts",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "preferred_channel",
                table: "contacts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "salutation",
                table: "contacts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_number",
                table: "contacts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "contacts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vat_id",
                table: "contacts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "website",
                table: "contacts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
            // Bisheriges Telefon / E-Mail wird die erste Nummer bzw. Adresse der Listen.
            migrationBuilder.Sql("""
                UPDATE contacts SET phones = jsonb_build_array(jsonb_build_object('kind', 'landline', 'number', phone)) WHERE phone IS NOT NULL AND phone <> '';
                UPDATE contacts SET emails = jsonb_build_array(jsonb_build_object('address', email)) WHERE email IS NOT NULL AND email <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "billing_address",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "birthday",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "delivery_address",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "emails",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "phones",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "preferred_channel",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "salutation",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "tax_number",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "title",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "vat_id",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "website",
                table: "contacts");
        }
    }
}
