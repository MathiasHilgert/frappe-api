using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frappe.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionStepUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "step_up_expires_at",
                schema: "identity",
                table: "sessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "step_up_expires_at",
                schema: "identity",
                table: "sessions");
        }
    }
}
