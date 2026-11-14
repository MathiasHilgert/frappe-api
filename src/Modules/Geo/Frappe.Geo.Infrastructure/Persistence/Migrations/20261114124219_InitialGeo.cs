using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Frappe.Geo.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialGeo : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "geo");

        migrationBuilder.CreateTable(
            name: "countries",
            schema: "geo",
            columns: table => new
            {
                code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                alpha3 = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                numeric = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                retired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_countries", x => x.code);
            });

        migrationBuilder.CreateTable(
            name: "currencies",
            schema: "geo",
            columns: table => new
            {
                code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                minor_unit_digits = table.Column<int>(type: "integer", nullable: false),
                retired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_currencies", x => x.code);
            });

        migrationBuilder.CreateTable(
            name: "inbox_messages",
            schema: "geo",
            columns: table => new
            {
                consumer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                message_id = table.Column<Guid>(type: "uuid", nullable: false),
                received_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inbox_messages", x => new { x.consumer, x.message_id });
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "geo",
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
                table.PrimaryKey("PK_outbox_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "country_currencies",
            schema: "geo",
            columns: table => new
            {
                currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                rank = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_country_currencies", x => new { x.country_code, x.currency_code });
                table.ForeignKey(
                    name: "FK_country_currencies_countries_country_code",
                    column: x => x.country_code,
                    principalSchema: "geo",
                    principalTable: "countries",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "country_names",
            schema: "geo",
            columns: table => new
            {
                language = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_country_names", x => new { x.country_code, x.language });
                table.ForeignKey(
                    name: "FK_country_names_countries_country_code",
                    column: x => x.country_code,
                    principalSchema: "geo",
                    principalTable: "countries",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "currency_names",
            schema: "geo",
            columns: table => new
            {
                language = table.Column<string>(type: "character varying(35)", maxLength: 35, nullable: false),
                currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_currency_names", x => new { x.currency_code, x.language });
                table.ForeignKey(
                    name: "FK_currency_names_currencies_currency_code",
                    column: x => x.currency_code,
                    principalSchema: "geo",
                    principalTable: "currencies",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_inbox_messages_received_at",
            schema: "geo",
            table: "inbox_messages",
            column: "received_at");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_pending",
            schema: "geo",
            table: "outbox_messages",
            column: "sequence",
            filter: "published_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_published_at",
            schema: "geo",
            table: "outbox_messages",
            column: "published_at",
            filter: "published_at IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "country_currencies",
            schema: "geo");

        migrationBuilder.DropTable(
            name: "country_names",
            schema: "geo");

        migrationBuilder.DropTable(
            name: "currency_names",
            schema: "geo");

        migrationBuilder.DropTable(
            name: "inbox_messages",
            schema: "geo");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "geo");

        migrationBuilder.DropTable(
            name: "countries",
            schema: "geo");

        migrationBuilder.DropTable(
            name: "currencies",
            schema: "geo");
    }
}
