using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EstrichOne.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ValueListsAndForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_schemas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_schemas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "value_list_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    list = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    short_label = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_value_list_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_schemas_key",
                table: "form_schemas",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_value_list_items_list_key",
                table: "value_list_items",
                columns: new[] { "list", "key" },
                unique: true);

            // Änderungsfeed für den Sync (siehe Migration Sync, record_change()).
            migrationBuilder.Sql("""
                CREATE TRIGGER value_list_items_change_log AFTER INSERT OR UPDATE OR DELETE ON value_list_items
                    FOR EACH ROW EXECUTE FUNCTION record_change('valueListItem', '');
                CREATE TRIGGER form_schemas_change_log AFTER INSERT OR UPDATE OR DELETE ON form_schemas
                    FOR EACH ROW EXECUTE FUNCTION record_change('formSchema', '');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS value_list_items_change_log ON value_list_items; DROP TRIGGER IF EXISTS form_schemas_change_log ON form_schemas; DELETE FROM change_log WHERE entity_type IN ('valueListItem', 'formSchema');");
            migrationBuilder.DropTable(
                name: "form_schemas");

            migrationBuilder.DropTable(
                name: "value_list_items");
        }
    }
}
