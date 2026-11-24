using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frappe.Notifications.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds three invariants as CHECK constraints (DATA-07) and documents the address columns as a copy of <c>recipient_key</c> (DATA-12): a
/// sent-like delivery has its <c>address_hash</c>, a delivered one has its <c>delivered_at</c>, and an event recorded as matched carries no
/// provider message id (only a parked one does). Each constraint is added <c>NOT VALID</c> (MIG-04), which takes a short ACCESS EXCLUSIVE
/// lock bounded by <c>lock_timeout</c>, and then validated in its own statement outside the transaction, which only takes a SHARE UPDATE
/// EXCLUSIVE lock, so reads and writes go on while it scans. A row that already breaks a rule makes its validation fail and names the
/// constraint; nothing is deleted or rewritten. The comments are catalog-only. Both tables are small (the module is new and its events are
/// purged after seven days), hence one migration. Rerunnable: the add drops its constraint first (its own statement, which carries the squawk
/// ignore) and a validation repeats harmlessly. The
/// <c>Down</c> drops the constraints and the comments and touches no column, so the <c>xmin</c> system column is never dropped. Rollout class:
/// rolling with a brief stall (the lock waits are bounded). Binaries from before still write valid rows except a sent-like row without a hash,
/// which the dispatcher never wrote.
/// </summary>
public partial class EnforceDeliveryInvariants : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
        // The drop only clears this constraint so a rerun re-adds it (MIG-04); squawk cannot be silenced inside a multi-action ALTER.
        migrationBuilder.Sql(
            """
            -- squawk-ignore ban-drop-constraint
            ALTER TABLE notifications.deliveries DROP CONSTRAINT IF EXISTS ck_deliveries_sent_like_hash;
            """);
        migrationBuilder.Sql(
            """
            ALTER TABLE notifications.deliveries
              ADD CONSTRAINT ck_deliveries_sent_like_hash
                CHECK (status NOT IN ('sent', 'delivered', 'delayed', 'bounced', 'complained') OR address_hash IS NOT NULL) NOT VALID;
            """);
        // The drop only clears this constraint so a rerun re-adds it (MIG-04); squawk cannot be silenced inside a multi-action ALTER.
        migrationBuilder.Sql(
            """
            -- squawk-ignore ban-drop-constraint
            ALTER TABLE notifications.deliveries DROP CONSTRAINT IF EXISTS ck_deliveries_status_delivered_at;
            """);
        migrationBuilder.Sql(
            """
            ALTER TABLE notifications.deliveries
              ADD CONSTRAINT ck_deliveries_status_delivered_at
                CHECK (status <> 'delivered' OR delivered_at IS NOT NULL) NOT VALID;
            """);
        // The drop only clears this constraint so a rerun re-adds it (MIG-04); squawk cannot be silenced inside a multi-action ALTER.
        migrationBuilder.Sql(
            """
            -- squawk-ignore ban-drop-constraint
            ALTER TABLE notifications.provider_events DROP CONSTRAINT IF EXISTS ck_provider_events_parked_unmatched;
            """);
        migrationBuilder.Sql(
            """
            ALTER TABLE notifications.provider_events
              ADD CONSTRAINT ck_provider_events_parked_unmatched
                CHECK (NOT matched OR provider_message_id IS NULL) NOT VALID;
            """);
        migrationBuilder.Sql(
            "COMMENT ON COLUMN notifications.deliveries.address_hash IS "
            + "'DATA-12 copy: the hash part of recipient_key (addr:<key id>:<hex>) for an address recipient; written with it, never updated.';");
        migrationBuilder.Sql(
            "COMMENT ON COLUMN notifications.deliveries.hash_key_id IS "
            + "'DATA-12 copy: the key id part of recipient_key (addr:<key id>:<hex>) for an address recipient; written with it, never updated.';");

        migrationBuilder.Sql(
            "ALTER TABLE notifications.deliveries VALIDATE CONSTRAINT ck_deliveries_sent_like_hash;", suppressTransaction: true);
        migrationBuilder.Sql(
            "ALTER TABLE notifications.deliveries VALIDATE CONSTRAINT ck_deliveries_status_delivered_at;", suppressTransaction: true);
        migrationBuilder.Sql(
            "ALTER TABLE notifications.provider_events VALIDATE CONSTRAINT ck_provider_events_parked_unmatched;", suppressTransaction: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("SET LOCAL lock_timeout = '5s';");
        migrationBuilder.Sql("ALTER TABLE notifications.provider_events DROP CONSTRAINT IF EXISTS ck_provider_events_parked_unmatched;");
        migrationBuilder.Sql("ALTER TABLE notifications.deliveries DROP CONSTRAINT IF EXISTS ck_deliveries_status_delivered_at;");
        migrationBuilder.Sql("ALTER TABLE notifications.deliveries DROP CONSTRAINT IF EXISTS ck_deliveries_sent_like_hash;");
        migrationBuilder.Sql("COMMENT ON COLUMN notifications.deliveries.hash_key_id IS NULL;");
        migrationBuilder.Sql("COMMENT ON COLUMN notifications.deliveries.address_hash IS NULL;");
    }
}
