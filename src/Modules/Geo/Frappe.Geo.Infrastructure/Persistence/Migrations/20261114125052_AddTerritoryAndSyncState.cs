using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Frappe.Geo.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTerritoryAndSyncState : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterDatabase()
            .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

        migrationBuilder.CreateTable(
            name: "administrative_areas",
            schema: "geo",
            columns: table => new
            {
                country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                source_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                subdivision_code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                level = table.Column<int>(type: "integer", nullable: false),
                category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                is_local_government = table.Column<bool>(type: "boolean", nullable: false),
                latitude = table.Column<double>(type: "double precision", nullable: true),
                longitude = table.Column<double>(type: "double precision", nullable: true),
                retired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_administrative_areas", x => new { x.country_code, x.source_id });
            });

        migrationBuilder.CreateTable(
            name: "localities",
            schema: "geo",
            columns: table => new
            {
                country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                source_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                subdivision_code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                area_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                local_government_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                search_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                latitude = table.Column<double>(type: "double precision", nullable: true),
                longitude = table.Column<double>(type: "double precision", nullable: true),
                retired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_localities", x => new { x.country_code, x.source_id });
            });

        migrationBuilder.CreateTable(
            name: "subdivisions",
            schema: "geo",
            columns: table => new
            {
                code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                country_code = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                source_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                latitude = table.Column<double>(type: "double precision", nullable: true),
                longitude = table.Column<double>(type: "double precision", nullable: true),
                retired_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subdivisions", x => x.code);
            });

        migrationBuilder.CreateTable(
            name: "sync_resources",
            schema: "geo",
            columns: table => new
            {
                source_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                resource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                etag = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                last_modified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                last_attempt_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                last_success_at = table.Column<Instant>(type: "timestamp with time zone", nullable: true),
                last_outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_resources", x => new { x.source_id, x.resource });
            });

        migrationBuilder.CreateTable(
            name: "sync_runs",
            schema: "geo",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                source_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                started_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                finished_at = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                inserted = table.Column<int>(type: "integer", nullable: false),
                updated = table.Column<int>(type: "integer", nullable: false),
                retired = table.Column<int>(type: "integer", nullable: false),
                revived = table.Column<int>(type: "integer", nullable: false),
                error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                conflicts = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_runs", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "subdivision_aliases",
            schema: "geo",
            columns: table => new
            {
                alias = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                subdivision_code = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                search_form = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                manual = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subdivision_aliases", x => new { x.subdivision_code, x.alias });
                table.ForeignKey(
                    name: "FK_subdivision_aliases_subdivisions_subdivision_code",
                    column: x => x.subdivision_code,
                    principalSchema: "geo",
                    principalTable: "subdivisions",
                    principalColumn: "code",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_administrative_areas_subdivision_level",
            schema: "geo",
            table: "administrative_areas",
            columns: new[] { "subdivision_code", "level" });

        migrationBuilder.CreateIndex(
            name: "ix_localities_search_name_trgm",
            schema: "geo",
            table: "localities",
            column: "search_name")
            .Annotation("Npgsql:IndexMethod", "gin")
            .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

        migrationBuilder.CreateIndex(
            name: "ix_localities_subdivision_search_name",
            schema: "geo",
            table: "localities",
            columns: new[] { "subdivision_code", "search_name" });

        migrationBuilder.CreateIndex(
            name: "ix_subdivision_aliases_search_form",
            schema: "geo",
            table: "subdivision_aliases",
            column: "search_form");

        migrationBuilder.CreateIndex(
            name: "ux_subdivision_aliases_subdivision_search_form",
            schema: "geo",
            table: "subdivision_aliases",
            columns: new[] { "subdivision_code", "search_form" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_subdivisions_country_code",
            schema: "geo",
            table: "subdivisions",
            column: "country_code");

        migrationBuilder.CreateIndex(
            name: "ix_sync_runs_source_started",
            schema: "geo",
            table: "sync_runs",
            columns: new[] { "source_id", "started_at" },
            descending: new[] { false, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "administrative_areas",
            schema: "geo");

        migrationBuilder.DropTable(
            name: "localities",
            schema: "geo");

        migrationBuilder.DropTable(
            name: "subdivision_aliases",
            schema: "geo");

        migrationBuilder.DropTable(
            name: "sync_resources",
            schema: "geo");

        migrationBuilder.DropTable(
            name: "sync_runs",
            schema: "geo");

        migrationBuilder.DropTable(
            name: "subdivisions",
            schema: "geo");

        migrationBuilder.AlterDatabase()
            .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
    }
}
