using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Frappe.Notifications.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds <c>send_requests</c>: one row per accepted explicit send request (S2), holding its identity, kind and instant and never its
/// payload (S13). The row exists so the request's event reaches the outbox in the same transaction (EVT-04) and so a retried request with
/// the same identity collapses on the key. A new table: no existing row to scan and no live reader to block. Rollout class:
/// rolling-safe, since no binary reads it before the Migrator has created it.
/// </summary>
public partial class AddSendRequests : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "send_requests",
            schema: "notifications",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                requested_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_send_requests", x => x.id);
                table.CheckConstraint("ck_send_requests_kind_format", "kind ~ '^[a-z][a-z0-9_]*$'");
            });

        migrationBuilder.CreateIndex(
            name: "ix_send_requests_requested_at",
            schema: "notifications",
            table: "send_requests",
            column: "requested_at");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "send_requests",
            schema: "notifications");
    }
}
