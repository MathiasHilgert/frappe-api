using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Frappe.Geo.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddManualAliasCountryUniqueness : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // One manual alias per country and search form: two administrators adding the same alias to two subdivisions
        // of a country at the same instant both pass the application's check, and only this index stops the second.
        // Manual aliases only: the source's (imported) aliases may repeat across subdivisions, and the territory sync
        // never writes a manual row, so no sync can fail on it. The country is the part of the code before the dash
        // ("AR" of "AR-C"), as everywhere else in Geo. Rows already sharing one are an administrator's to settle:
        // nothing is deleted, the migration stops and names them instead.
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                shared text;
            BEGIN
                SELECT string_agg(holders, '; ' ORDER BY holders) INTO shared
                FROM (
                    SELECT string_agg(subdivision_code || ' ' || quote_literal(alias), ', ' ORDER BY subdivision_code) AS holders
                    FROM geo.subdivision_aliases
                    WHERE manual
                    GROUP BY split_part(subdivision_code, '-', 1), search_form
                    HAVING count(*) > 1) AS duplicates;
                IF shared IS NOT NULL THEN
                    RAISE EXCEPTION 'Manual subdivision aliases shared by two subdivisions of a country: %. Remove all but one of each and run the migration again.', shared;
                END IF;
            END
            $$;

            CREATE UNIQUE INDEX ux_subdivision_aliases_manual_country_search_form
                ON geo.subdivision_aliases (split_part(subdivision_code, '-', 1), search_form)
                WHERE manual;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX geo.ux_subdivision_aliases_manual_country_search_form;");
    }
}
