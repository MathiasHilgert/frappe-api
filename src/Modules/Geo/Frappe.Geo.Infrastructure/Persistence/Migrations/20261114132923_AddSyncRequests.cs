using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Frappe.Geo.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSyncRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sync_requests",
            schema: "geo",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                requested_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_requests", x => x.id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "sync_requests",
            schema: "geo");
    }
}
