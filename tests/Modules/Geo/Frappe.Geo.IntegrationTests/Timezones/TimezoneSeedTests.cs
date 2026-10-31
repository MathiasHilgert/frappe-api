using Frappe.Geo.Infrastructure;
using Frappe.Migrations;
using NodaTime.TimeZones;
using Npgsql;

namespace Frappe.Geo.IntegrationTests.Timezones;

// The fixture's database was seeded once, as the migrator seeds every deployment.
public sealed class TimezoneSeedTests(AppFixture app)
{
    [Fact]
    public async Task Seeding_the_catalogue_a_second_time_changes_no_row()
    {
        string[] before = await RowsAsync();

        int changed = await MigrationsRunner.SeedAllAsync(
            app.OwnerConnectionString,
            [new GeoModule()],
            TestContext.Current.CancellationToken
        );

        changed.ShouldBe(0);
        (await RowsAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task Every_id_of_the_tzdb_release_is_in_the_catalogue()
    {
        string[] names = await ScalarAsync<string[]>(
            "SELECT array_agg(name ORDER BY name) FROM geo.timezones"
        );

        names.ShouldBe(TzdbDateTimeZoneSource.Default.CanonicalIdMap.Keys, ignoreOrder: true);
    }

    [Fact]
    public async Task Every_alias_points_at_a_canonical_zone()
    {
        long[] aliases = await ScalarAsync<long[]>(
            """
            SELECT ARRAY[count(*), count(*) FILTER (WHERE canonical.canonical_name IS NOT NULL)]
            FROM geo.timezones alias
            JOIN geo.timezones canonical ON canonical.name = alias.canonical_name
            """
        );

        aliases[0].ShouldBeGreaterThan(0);
        aliases[1].ShouldBe(0);
    }

    [Fact]
    public async Task The_app_role_can_only_read_the_catalogue()
    {
        bool[] privileges = await ScalarAsync<bool[]>(
            """
            SELECT ARRAY[
                has_table_privilege('frappe_app', 'geo.timezones', 'SELECT'),
                has_table_privilege('frappe_app', 'geo.timezones', 'INSERT'),
                has_table_privilege('frappe_app', 'geo.timezones', 'UPDATE'),
                has_table_privilege('frappe_app', 'geo.timezones', 'DELETE')]
            """
        );

        privileges.ShouldBe([true, false, false, false]);
    }

    private Task<string[]> RowsAsync() =>
        ScalarAsync<string[]>(
            "SELECT array_agg(name || '=' || coalesce(canonical_name, '') ORDER BY name) FROM geo.timezones"
        );

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using NpgsqlConnection connection = new(app.OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = new(sql, connection);
        object? value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return value is T typed
            ? typed
            : throw new InvalidOperationException($"The query returned {value ?? "null"}.");
    }
}
