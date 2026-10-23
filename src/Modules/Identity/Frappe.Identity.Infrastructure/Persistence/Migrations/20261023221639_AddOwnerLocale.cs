using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frappe.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerLocale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Owners registered before locales existed read the platform default.
            migrationBuilder.AddColumn<string>(
                name: "locale",
                schema: "identity",
                table: "owner_accounts",
                type: "character varying(35)",
                maxLength: 35,
                nullable: false,
                defaultValue: "en");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "locale",
                schema: "identity",
                table: "owner_accounts");
        }
    }
}
