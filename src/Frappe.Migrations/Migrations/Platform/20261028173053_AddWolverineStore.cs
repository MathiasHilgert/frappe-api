using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frappe.Migrations.Migrations.Platform
{
    /// <inheritdoc />
    public partial class AddWolverineStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(EmbeddedSql.Read("AddWolverineStore.sql"));
        }
    }
}
