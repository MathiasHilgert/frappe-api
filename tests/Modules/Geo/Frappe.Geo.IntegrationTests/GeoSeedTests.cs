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
    public async Task Every_active_row_has_an_english_name(string unnamedRows)
    {
        long unnamed = await ScalarAsync<long>(unnamedRows);

        unnamed.ShouldBe(0);
    }

    [Fact]
    public async Task Holidays_are_seeded_for_argentina_in_2026_and_2027_only()
    {
        string seeded = await ScalarAsync<string>(
            "SELECT string_agg(DISTINCT country_code || ' ' || extract(year FROM date), ', ') FROM geo.holidays"
        );

        seeded.ShouldBe("AR 2026, AR 2027");
    }

    [Fact]
    public async Task Every_holiday_has_a_spanish_english_and_portuguese_name()
    {
        long incomplete = await ScalarAsync<long>(
            "SELECT count(*) FROM geo.holidays h WHERE (SELECT count(*) FROM geo.holiday_names n "
                + "WHERE n.holiday_id = h.holiday_id AND n.locale IN ('es', 'en', 'pt')) <> 3"
        );

        incomplete.ShouldBe(0);
    }

    [Fact]
    public async Task A_calendar_the_file_does_not_hold_is_kept_even_in_a_seeded_year()
    {
        await ExecuteAsync(
            "INSERT INTO geo.holidays (holiday_id, country_code, date, holiday_key, class, origin, status) "
                + "VALUES (uuidv7(), 'UY', '2026-07-18', 'constitution-day', 'Holiday', 'Fixed', 'Active')"
        );
        try
        {
            await MigrationsRunner.SeedAllAsync(
                geo.OwnerConnectionString,
                [new GeoModule()],
                TestContext.Current.CancellationToken
            );

            string status = await ScalarAsync<string>(
                "SELECT status FROM geo.holidays WHERE country_code = 'UY'"
            );
            status.ShouldBe("Active");
        }
        finally
        {
            await ExecuteAsync("DELETE FROM geo.holidays WHERE country_code = 'UY'");
        }
    }

    private async Task ExecuteAsync(string sql)
    {
        await using NpgsqlConnection connection = new(geo.OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
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
