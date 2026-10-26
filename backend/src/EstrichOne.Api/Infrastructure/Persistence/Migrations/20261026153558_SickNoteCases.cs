using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SickNoteCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "case_id",
                table: "absences",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recorded_at",
                table: "absences",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_absences_case_id",
                table: "absences",
                column: "case_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_absences_case_id",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "case_id",
                table: "absences");

            migrationBuilder.DropColumn(
                name: "recorded_at",
                table: "absences");
        }
    }
}
