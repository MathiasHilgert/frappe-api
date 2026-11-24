using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Frappe.Identity.Infrastructure.Persistence.Migrations;

/// <summary>
/// Creates the module's schema with its tables: <c>principals</c>, <c>email_identifiers</c>, <c>password_credentials</c>, <c>sessions</c>,
/// <c>one_time_codes</c>, <c>email_change_undos</c> and <c>owners</c>, and the outbox and inbox every module owns. Everything is new, so the
/// CHECKs and indexes are built with the tables: there is no existing row to scan and no live reader to block, and it takes no lock on any
/// table that already exists. Not
/// rerunnable after a partial failure by itself (the migration runs in one transaction, so a failure leaves nothing). Rollout class:
/// rolling-safe, since no binary reads these tables before the Migrator has created them.
/// </summary>
public partial class CreateIdentitySchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "identity");

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "identity",
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
            schema: "identity",
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
                    "published_at IS NULL OR (leased_until IS NULL AND last_error IS NULL)");
            });

        migrationBuilder.CreateTable(
            name: "principals",
            schema: "identity",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                status = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                credential_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_principals", x => x.id);
                table.CheckConstraint("ck_principals_kind", "kind IN ('owner')");
                table.CheckConstraint("ck_principals_status", "status IN ('pending', 'active')");
            });

        migrationBuilder.CreateTable(
            name: "email_identifiers",
            schema: "identity",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                normalized_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false,
                    comment: "DATA-12 copy: address trimmed and lower-cased; written with it by the Email value object, never updated alone."),
                verified_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_email_identifiers", x => x.id);
                table.ForeignKey(
                    name: "fk_email_identifiers_principal_id",
                    column: x => x.principal_id,
                    principalSchema: "identity",
                    principalTable: "principals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "one_time_codes",
            schema: "identity",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                purpose = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: false),
                target = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                code_hmac = table.Column<byte[]>(type: "bytea", nullable: false),
                issued_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                redeemed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                invalidated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_one_time_codes", x => x.id);
                table.CheckConstraint("ck_one_time_codes_code_hmac_length", "octet_length(code_hmac) = 32");
                table.CheckConstraint("ck_one_time_codes_expires_after_issued", "expires_at > issued_at");
                table.CheckConstraint("ck_one_time_codes_invalidated_after_issued", "invalidated_at IS NULL OR invalidated_at >= issued_at");
                table.CheckConstraint("ck_one_time_codes_purpose", "purpose IN ('email_verification', 'password_reset', 'email_change')");
                table.CheckConstraint(
                    "ck_one_time_codes_redeemed_in_window",
                    "redeemed_at IS NULL OR (redeemed_at >= issued_at AND redeemed_at <= expires_at)");
                table.ForeignKey(
                    name: "fk_one_time_codes_principal_id",
                    column: x => x.principal_id,
                    principalSchema: "identity",
                    principalTable: "principals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "email_change_undos",
            schema: "identity",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                old_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                new_address = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                token_hmac = table.Column<byte[]>(type: "bytea", nullable: false),
                issued_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                redeemed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                invalidated_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_email_change_undos", x => x.id);
                table.CheckConstraint("ck_email_change_undos_addresses_differ", "old_address <> new_address");
                table.CheckConstraint("ck_email_change_undos_expires_after_issued", "expires_at > issued_at");
                table.CheckConstraint("ck_email_change_undos_invalidated_after_issued", "invalidated_at IS NULL OR invalidated_at >= issued_at");
                table.CheckConstraint(
                    "ck_email_change_undos_redeemed_in_window",
                    "redeemed_at IS NULL OR (redeemed_at >= issued_at AND redeemed_at <= expires_at)");
                table.CheckConstraint("ck_email_change_undos_token_hmac_length", "octet_length(token_hmac) = 32");
                table.ForeignKey(
                    name: "fk_email_change_undos_principal_id",
                    column: x => x.principal_id,
                    principalSchema: "identity",
                    principalTable: "principals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "owners",
            schema: "identity",
            columns: table => new
            {
                principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                given_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                family_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                locale = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                registered_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                verified_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_owners", x => x.principal_id);
                table.CheckConstraint("ck_owners_verified_after_registered", "verified_at >= registered_at");
                table.ForeignKey(
                    name: "fk_owners_principal_id",
                    column: x => x.principal_id,
                    principalSchema: "identity",
                    principalTable: "principals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "password_credentials",
            schema: "identity",
            columns: table => new
            {
                principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                changed_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_password_credentials", x => x.principal_id);
                table.ForeignKey(
                    name: "fk_password_credentials_principal_id",
                    column: x => x.principal_id,
                    principalSchema: "identity",
                    principalTable: "principals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "sessions",
            schema: "identity",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                principal_id = table.Column<Guid>(type: "uuid", nullable: false),
                token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                audience = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                methods = table.Column<string[]>(type: "text[]", nullable: false),
                credential_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                opened_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                last_seen_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                idle_expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                absolute_expires_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                ended_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                ip_prefix = table.Column<IPNetwork>(type: "cidr", nullable: true),
                step_up_until = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                revoked_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                revocation_reason = table.Column<string>(type: "character varying(19)", maxLength: 19, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_sessions", x => x.id);
                table.CheckConstraint("ck_sessions_audience", "audience IN ('console')");
                table.CheckConstraint("ck_sessions_ended_after_revoked", "ended_at >= revoked_at");
                table.CheckConstraint("ck_sessions_idle_within_absolute", "idle_expires_at <= absolute_expires_at");
                table.CheckConstraint(
                    "ck_sessions_ip_prefix",
                    "ip_prefix IS NULL OR (family(ip_prefix) = 4 AND masklen(ip_prefix) = 24) OR (family(ip_prefix) " +
                    "= 6 AND masklen(ip_prefix) = 48)");
                table.CheckConstraint("ck_sessions_methods", "methods <@ ARRAY['pwd']::text[] AND cardinality(methods) > 0");
                table.CheckConstraint("ck_sessions_revocation_pair", "(revoked_at IS NULL) = (revocation_reason IS NULL)");
                table.CheckConstraint(
                    "ck_sessions_revocation_reason",
                    "revocation_reason IN ('logout', 'revoked_by_holder', 'others_revoked', 'password_changed', " +
                    "'password_reset', 'email_changed', 'email_change_undone')");
                table.CheckConstraint("ck_sessions_seen_after_opened", "opened_at <= last_seen_at");
                table.CheckConstraint("ck_sessions_token_hash_length", "octet_length(token_hash) = 32");
                table.ForeignKey(
                    name: "fk_sessions_principal_id",
                    column: x => x.principal_id,
                    principalSchema: "identity",
                    principalTable: "principals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ux_email_identifiers_normalized_address",
            schema: "identity",
            table: "email_identifiers",
            column: "normalized_address",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_email_identifiers_principal_id",
            schema: "identity",
            table: "email_identifiers",
            column: "principal_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_inbox_messages_received_at",
            schema: "identity",
            table: "inbox_messages",
            column: "received_at");

        migrationBuilder.CreateIndex(
            name: "ix_email_change_undos_expires_at",
            schema: "identity",
            table: "email_change_undos",
            column: "expires_at");

        migrationBuilder.CreateIndex(
            name: "ux_email_change_undos_live",
            schema: "identity",
            table: "email_change_undos",
            columns: new[] { "principal_id", "old_address", "new_address" },
            unique: true,
            filter: "redeemed_at IS NULL AND invalidated_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_one_time_codes_expires_at",
            schema: "identity",
            table: "one_time_codes",
            column: "expires_at");

        migrationBuilder.CreateIndex(
            name: "ix_one_time_codes_principal_id",
            schema: "identity",
            table: "one_time_codes",
            column: "principal_id");

        migrationBuilder.CreateIndex(
            name: "ux_one_time_codes_live",
            schema: "identity",
            table: "one_time_codes",
            columns: new[] { "principal_id", "purpose" },
            unique: true,
            filter: "redeemed_at IS NULL AND invalidated_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_parked",
            schema: "identity",
            table: "outbox_messages",
            column: "sequence",
            filter: "published_at IS NULL AND attempts > 0 AND next_attempt_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_published_at",
            schema: "identity",
            table: "outbox_messages",
            column: "published_at",
            filter: "published_at IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_relayable",
            schema: "identity",
            table: "outbox_messages",
            column: "sequence",
            filter: "published_at IS NULL AND (attempts = 0 OR next_attempt_at IS NOT NULL)");

        migrationBuilder.CreateIndex(
            name: "ix_owners_registered_at",
            schema: "identity",
            table: "owners",
            column: "registered_at",
            filter: "verified_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_sessions_ended_at",
            schema: "identity",
            table: "sessions",
            column: "ended_at",
            filter: "ended_at IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_sessions_principal_id",
            schema: "identity",
            table: "sessions",
            column: "principal_id");

        migrationBuilder.CreateIndex(
            name: "ux_sessions_token_hash",
            schema: "identity",
            table: "sessions",
            column: "token_hash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "email_identifiers",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "email_change_undos",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "one_time_codes",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "owners",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "password_credentials",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "sessions",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "principals",
            schema: "identity");
    }
}
