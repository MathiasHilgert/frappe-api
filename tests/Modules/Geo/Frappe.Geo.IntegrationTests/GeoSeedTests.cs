using Frappe.Geo.Contracts.ValueObjects;
using Frappe.Geo.Infrastructure;
using Frappe.Migrations;
using Npgsql;

namespace Frappe.Geo.IntegrationTests;

// The fixture seeded the database once; these read what it left, as the owner.
public sealed class GeoSeedTests(GeoFixture geo)
{
    [Fact]
    public async Task Seeding_a_seeded_database_changes_nothing()
    {
        int changed = await MigrationsRunner.SeedAllAsync(
            geo.OwnerConnectionString,
            [new GeoModule()],
            TestContext.Current.CancellationToken
        );

        changed.ShouldBe(0);
    }

    [Theory]
    [InlineData("BGN", "Retired")]
    [InlineData("EUR", "Active")]
    [InlineData("ARS", "Active")]
    public async Task A_withdrawn_currency_is_kept_as_retired(string code, string status)
    {
        string stored = await ScalarAsync<string>(
            "SELECT status FROM geo.currencies WHERE currency_code = @code",
            code
        );

        stored.ShouldBe(status);
    }

    [Fact]
    public async Task Every_iso_3166_1_country_is_active()
    {
        long active = await ScalarAsync<long>(
            "SELECT count(*) FROM geo.countries WHERE status = 'Active'"
        );

        active.ShouldBe(249);
    }

    [Theory]
    [InlineData(
        "SELECT count(*) FROM geo.currencies c WHERE c.status = 'Active' "
            + "AND NOT EXISTS (SELECT 1 FROM geo.currency_names n WHERE n.currency_code = c.currency_code AND n.locale = 'en')"
    )]
    [InlineData(
        "SELECT count(*) FROM geo.countries c WHERE c.status = 'Active' "
            + "AND NOT EXISTS (SELECT 1 FROM geo.country_names n WHERE n.country_code = c.country_code AND n.locale = 'en')"
    )]
    [InlineData(
        "SELECT count(*) FROM geo.subdivisions s WHERE s.status = 'Active' "
            + "AND NOT EXISTS (SELECT 1 FROM geo.subdivision_names n WHERE n.subdivision_code = s.subdivision_code AND n.locale = 'en')"
    )]
    [InlineData(
        "SELECT count(*) FROM geo.locales l WHERE l.status = 'Active' "
            + "AND NOT EXISTS (SELECT 1 FROM geo.locale_names n WHERE n.locale_tag = l.locale_tag AND n.locale = 'en')"
    )]
    public async Task Every_active_row_has_an_english_name(string unnamedRows)
    {
        long unnamed = await ScalarAsync<long>(unnamedRows);

        unnamed.ShouldBe(0);
    }

    [Fact]
    public async Task Subdivisions_are_the_24_argentine_provinces_only()
    {
        long provinces = await ScalarAsync<long>(
            "SELECT count(*) FROM geo.subdivisions WHERE status = 'Active' AND country_code = 'AR'"
        );
        long others = await ScalarAsync<long>(
            "SELECT count(*) FROM geo.subdivisions WHERE country_code <> 'AR'"
        );

        provinces.ShouldBe(24);
        others.ShouldBe(0);
    }

    [Fact]
    public async Task Cities_are_the_georef_localities_of_argentina_with_unique_indec_ids()
    {
        long active = await ScalarAsync<long>(
            "SELECT count(*) FROM geo.cities WHERE status = 'Active' AND country_code = 'AR'"
        );
        long others = await ScalarAsync<long>(
            "SELECT count(*) FROM geo.cities WHERE country_code <> 'AR'"
        );
        long repeated = await ScalarAsync<long>(
            "SELECT count(*) - count(DISTINCT indec_id) FROM geo.cities"
        );

        active.ShouldBeGreaterThanOrEqualTo(4000);
        others.ShouldBe(0);
        repeated.ShouldBe(0);
    }

    [Fact]
    public async Task Every_city_zone_is_one_of_the_argentine_province_zones()
    {
        string[] zones = await ScalarAsync<string[]>(
            "SELECT array_agg(DISTINCT time_zone_id ORDER BY time_zone_id) FROM geo.cities"
        );

        zones.Length.ShouldBeLessThanOrEqualTo(12);
        zones.ShouldAllBe(zone =>
            zone.StartsWith("America/Argentina/", StringComparison.Ordinal)
            && !TimeZoneId.Create(zone).IsError
        );
    }

    [Fact]
    public async Task Every_seeded_locale_tag_is_a_canonical_locale()
    {
        string[] tags = await ScalarAsync<string[]>(
            "SELECT array_agg(locale_tag) FROM geo.locales"
        );

        tags.ShouldAllBe(tag => Locale.Create(tag).Value.Value == tag);
    }

    // A changed province name changes the search text the seed writes, so the next seed rewrites those cities.
    [Fact]
    public async Task A_stale_search_text_is_rewritten_and_counted_by_the_next_seed()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using NpgsqlConnection connection = new(geo.OwnerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            cancellationToken
        );
        await using (
            NpgsqlCommand stale = new(
                "UPDATE geo.cities SET search_text = search_name WHERE indec_id IN ('14042170', '14119160')",
                connection
            )
        )
        {
            await stale.ExecuteNonQueryAsync(cancellationToken);
        }

        int changed = await new GeoModule().SeedAsync(connection, cancellationToken);
        await using NpgsqlCommand read = new(
            "SELECT search_text FROM geo.cities WHERE indec_id = '14042170'",
            connection
        );
        object? searchText = await read.ExecuteScalarAsync(cancellationToken);
        await transaction.RollbackAsync(cancellationToken);

        changed.ShouldBe(2);
        searchText.ShouldBe("villa maria cordoba argentina");
    }

    private async Task<T> ScalarAsync<T>(string sql, string? code = null)
    {
        await using NpgsqlConnection connection = new(geo.OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("code", (object?)code ?? DBNull.Value);
        object? value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is T typed
            ? typed
            : throw new InvalidOperationException($"The query returned {value ?? "null"}.");
    }
}
