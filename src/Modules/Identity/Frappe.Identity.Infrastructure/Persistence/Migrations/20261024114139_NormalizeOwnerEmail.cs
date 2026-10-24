using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frappe.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeOwnerEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Addresses are now normalized by the domain (trimmed, lower-cased); bring stored ones in line and replace
            // the lower(email) expression index with a plain unique one on the normalized column.
            migrationBuilder.Sql("DROP INDEX identity.owner_accounts_email_key;");
            migrationBuilder.Sql(
                "UPDATE identity.owner_accounts SET email = lower(btrim(email)), pending_email = lower(btrim(pending_email));");

            migrationBuilder.CreateIndex(
                name: "owner_accounts_email_key",
                schema: "identity",
                table: "owner_accounts",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "owner_accounts_email_key",
                schema: "identity",
                table: "owner_accounts");

            // Stored addresses stay lower-cased: the original casing is not recoverable.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX owner_accounts_email_key ON identity.owner_accounts (lower(email));");
        }
    }
}
