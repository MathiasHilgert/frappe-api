using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Frappe.Notifications.Infrastructure.Persistence.Migrations;

/// <summary>
/// Creates the module's schema with its tables: <c>deliveries</c>, <c>suppressions</c>, <c>preferences</c>, <c>provider_events</c>,
/// and the outbox and inbox every module owns. Everything is new, so the CHECKs and indexes are built with the tables: there is
/// no existing row to scan and no live reader to block, and it takes no lock on any table that already exists. Not rerunnable
/// after a partial failure by itself (the migration runs in one transaction, so a failure leaves nothing). Rollout class: rolling-safe,
/// since no binary reads these tables before the Migrator has created them.
/// </summary>
public partial class CreateNotificationsSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "notifications");

        migrationBuilder.CreateTable(
            name: "deliveries",
            schema: "notifications",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                category = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                recipient_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                address_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                hash_key_id = table.Column<short>(type: "smallint", nullable: true),
                locale_served = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: true),
                status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                failure_reason = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: true),
                provider = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                provider_message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                sent_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                delivered_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                status_changed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_deliveries", x => x.id);
                table.CheckConstraint("ck_deliveries_address_hash_length", "address_hash IS NULL OR octet_length(address_hash) = 32");
                table.CheckConstraint("ck_deliveries_address_hash_pair", "(address_hash IS NULL) = (hash_key_id IS NULL)");
                table.CheckConstraint("ck_deliveries_category", "category IN ('security', 'transactional', 'marketing')");
                table.CheckConstraint("ck_deliveries_failed_reason", "(status = 'failed') = (failure_reason IS NOT NULL)");
                table.CheckConstraint("ck_deliveries_failure_reason", "failure_reason IN ('provider_rejected', 'invalid_recipient')");
                table.CheckConstraint("ck_deliveries_kind_format", "kind ~ '^[a-z][a-z0-9_]*$'");
                table.CheckConstraint("ck_deliveries_provider", "provider IN ('resend')");
                table.CheckConstraint("ck_deliveries_provider_pair", "(provider IS NULL) = (provider_message_id IS NULL)");
                table.CheckConstraint("ck_deliveries_recipient_key_format", "recipient_key ~ '^(ref:[a-z_]+:.+|addr:[0-9]+:[0-9a-f]{64})$'");
                table.CheckConstraint(
                    "ck_deliveries_sent_like",
                    "status NOT IN ('sent', 'delivered', 'delayed', 'bounced', 'complained') OR " +
                    "(provider_message_id IS NOT NULL AND sent_at IS NOT NULL)");
                table.CheckConstraint(
                    "ck_deliveries_status",
                    "status IN ('sent', 'delivered', 'delayed', 'bounced', " +
                    "'complained', 'failed', 'unresolved')");
            });

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "notifications",
            columns: table => new
            {
                consumer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                received_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_inbox_messages", x => new { x.consumer, x.message_id });
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "notifications",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                sequence = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                data_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                data = table.Column<string>(type: "jsonb", nullable: false),
                occurred_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                trace_parent = table.Column<string>(type: "character varying(55)", maxLength: 55, nullable: true),
                trace_state = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                published_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                leased_until = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                attempts = table.Column<int>(type: "integer", nullable: false),
                next_attempt_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_messages", x => x.id);
                table.CheckConstraint("ck_outbox_messages_attempts_nonnegative", "attempts >= 0");
                table.CheckConstraint("ck_outbox_messages_last_error_after_failure", "last_error IS NULL OR attempts > 0");
                table.CheckConstraint("ck_outbox_messages_next_attempt_after_failure", "next_attempt_at IS NULL OR attempts > 0");
                table.CheckConstraint(
                    "ck_outbox_messages_published_settled",
                    "published_at IS NULL OR (leased_until IS " +
                    "NULL AND last_error IS NULL)");
            });

        migrationBuilder.CreateTable(
            name: "preferences",
            schema: "notifications",
            columns: table => new
            {
                recipient_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                category = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: false),
                opted_out_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_preferences", x => new { x.recipient_key, x.category });
                table.CheckConstraint("ck_preferences_only_marketing", "category = 'marketing'");
                table.CheckConstraint("ck_preferences_recipient_key_format", "recipient_key ~ '^(ref:[a-z_]+:.+|addr:[0-9]+:[0-9a-f]{64})$'");
            });

        migrationBuilder.CreateTable(
            name: "provider_events",
            schema: "notifications",
            columns: table => new
            {
                provider = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                received_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                matched = table.Column<bool>(type: "boolean", nullable: false),
                provider_message_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                feedback = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: true),
                occurred_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_provider_events", x => new { x.provider, x.event_id });
                table.CheckConstraint(
                    "ck_provider_events_feedback",
                    "feedback IN ('delivered', 'permanent_failure', " +
                    "'complaint', 'delayed', 'suppressed_by_provider')");
                table.CheckConstraint(
                    "ck_provider_events_parked_together",
                    "(provider_message_id IS NULL) = (feedback IS NULL) AND " +
                    "(feedback IS NULL) = (occurred_at IS NULL)");
                table.CheckConstraint("ck_provider_events_provider", "provider IN ('resend')");
            });

        migrationBuilder.CreateTable(
            name: "suppressions",
            schema: "notifications",
            columns: table => new
            {
                channel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                address_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                hash_key_id = table.Column<short>(type: "smallint", nullable: false),
                reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                source = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                provider = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: true),
                provider_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_suppressions", x => new { x.channel, x.address_hash });
                table.CheckConstraint("ck_suppressions_address_hash_length", "octet_length(address_hash) = 32");
                table.CheckConstraint("ck_suppressions_provider", "provider IN ('resend')");
                table.CheckConstraint("ck_suppressions_provider_pair", "(provider IS NULL) = (provider_event_id IS NULL)");
                table.CheckConstraint("ck_suppressions_reason", "reason IN ('hard_bounce', 'complaint', 'provider_suppression')");
                table.CheckConstraint("ck_suppressions_source", "source IN ('webhook', 'manual')");
            });

        migrationBuilder.CreateIndex(
            name: "ix_deliveries_created_at",
            schema: "notifications",
            table: "deliveries",
            column: "created_at");

        migrationBuilder.CreateIndex(
            name: "ux_deliveries_message_kind_recipient_channel",
            schema: "notifications",
            table: "deliveries",
            columns: new[] { "source_message_id", "kind", "recipient_key", "channel" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);

        migrationBuilder.CreateIndex(
            name: "ux_deliveries_provider_message",
            schema: "notifications",
            table: "deliveries",
            columns: new[] { "provider", "provider_message_id" },
            unique: true,
            filter: "provider_message_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_inbox_messages_received_at",
            schema: "notifications",
            table: "inbox_messages",
            column: "received_at");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_parked",
            schema: "notifications",
            table: "outbox_messages",
            column: "sequence",
            filter: "published_at IS NULL AND attempts > 0 AND next_attempt_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_published_at",
            schema: "notifications",
            table: "outbox_messages",
            column: "published_at",
            filter: "published_at IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_relayable",
            schema: "notifications",
            table: "outbox_messages",
            column: "sequence",
            filter: "published_at IS NULL AND (attempts = 0 OR next_attempt_at IS NOT NULL)");

        migrationBuilder.CreateIndex(
            name: "ix_provider_events_received_at",
            schema: "notifications",
            table: "provider_events",
            column: "received_at");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "deliveries",
            schema: "notifications");

        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "notifications");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "notifications");

        migrationBuilder.DropTable(
            name: "preferences",
            schema: "notifications");

        migrationBuilder.DropTable(
            name: "provider_events",
            schema: "notifications");

        migrationBuilder.DropTable(
            name: "suppressions",
            schema: "notifications");
    }
}
