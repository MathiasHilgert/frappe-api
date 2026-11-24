using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frappe.Notifications.Infrastructure.Persistence.Migrations;

/// <summary>
/// Maps <c>xmin</c> as the row version of <c>deliveries</c> (UOW-04; Postgres has the system column already, so the generated column
/// operation writes no SQL) and adds the partial index that finds the events parked for a message when its delivery is recorded (Flow 3).
/// Locks: the <c>xmin</c> mapping writes no SQL (the system column exists), and <c>CREATE INDEX</c> takes a SHARE lock on
/// <c>provider_events</c>, which blocks writes to it while the index builds. That table is created on this unreleased branch and only this
/// slice's webhook route writes to it, so wherever the migration first runs it is empty and the build is instant. Rerunnable: the
/// migration runs in one transaction and EF records it in its history, so a failed run leaves nothing behind and a successful one is
/// never applied twice. Rollout class: rolling-safe, since no binary needs the index to be correct, only to be fast.
/// </summary>
public partial class TrackDeliveryFeedback : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            schema: "notifications",
            table: "deliveries",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);

        migrationBuilder.CreateIndex(
            name: "ix_provider_events_provider_message",
            schema: "notifications",
            table: "provider_events",
            columns: new[] { "provider", "provider_message_id" },
            filter: "provider_message_id IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_provider_events_provider_message",
            schema: "notifications",
            table: "provider_events");

        migrationBuilder.DropColumn(
            name: "xmin",
            schema: "notifications",
            table: "deliveries");
    }
}
