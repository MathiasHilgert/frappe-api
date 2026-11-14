using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Frappe.Geo.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddHolidayCalendars : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "holiday_calendars",
            schema: "geo",
            columns: table => new
            {
                jurisdiction = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                year = table.Column<int>(type: "integer", nullable: false),
                scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                revision = table.Column<int>(type: "integer", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                attribution_license = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                attribution_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                attribution_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_holiday_calendars", x => new { x.jurisdiction, x.year, x.scope });
            });

        migrationBuilder.CreateTable(
            name: "calendar_entries",
            schema: "geo",
            columns: table => new
            {
                layer = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                date = table.Column<LocalDate>(type: "date", nullable: false),
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                jurisdiction = table.Column<string>(type: "character varying(6)", nullable: false),
                year = table.Column<int>(type: "integer", nullable: false),
                scope = table.Column<string>(type: "character varying(16)", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                moved_from = table.Column<LocalDate>(type: "date", nullable: true),
                community = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                legal_citation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                legal_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_calendar_entries", x => new { x.jurisdiction, x.year, x.scope, x.layer, x.date, x.name });
                table.ForeignKey(
                    name: "FK_calendar_entries_holiday_calendars_jurisdiction_year_scope",
                    columns: x => new { x.jurisdiction, x.year, x.scope },
                    principalSchema: "geo",
                    principalTable: "holiday_calendars",
                    principalColumns: new[] { "jurisdiction", "year", "scope" },
                    onDelete: ReferentialAction.Cascade);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "calendar_entries",
            schema: "geo");

        migrationBuilder.DropTable(
            name: "holiday_calendars",
            schema: "geo");
    }
}
