using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frappe.Migrations.Migrations.Platform
{
    /// <inheritdoc />
    public partial class AddPgTrgm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedSql.Read("AddPgTrgm.sql"));
        }
    }
}
