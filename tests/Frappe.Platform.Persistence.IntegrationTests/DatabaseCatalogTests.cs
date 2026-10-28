using Npgsql;

namespace Frappe.Platform.Persistence.IntegrationTests;

// What the roles and the applied migrations leave in the catalog, read as the owner.
public sealed class DatabaseCatalogTests(AppFixture app)
{
    [Fact]
    public async Task The_app_role_cannot_bypass_row_level_security()
    {
        bool bypassesRls = await ScalarAsync<bool>(
            "SELECT rolbypassrls FROM pg_roles WHERE rolname = 'frappe_app'"
        );

        bypassesRls.ShouldBeFalse();
    }

    [Fact]
    public async Task The_app_role_owns_no_relation_schema_or_database()
    {
        long owned = await ScalarAsync<long>(
            """
            SELECT (SELECT count(*) FROM pg_class WHERE relowner = r.oid)
                 + (SELECT count(*) FROM pg_namespace WHERE nspowner = r.oid)
                 + (SELECT count(*) FROM pg_database WHERE datdba = r.oid)
            FROM pg_roles r
            WHERE r.rolname = 'frappe_app'
            """
        );

        owned.ShouldBe(0);
    }

    [Fact]
    public async Task The_app_role_has_the_three_timeouts()
    {
        string[] settings = await ScalarAsync<string[]>(
            """
            SELECT s.setconfig
            FROM pg_db_role_setting s
            JOIN pg_roles r ON r.oid = s.setrole
            WHERE r.rolname = 'frappe_app' AND s.setdatabase = 0
            """
        );

        settings.ShouldBe(
            ["statement_timeout=15s", "lock_timeout=5s", "idle_in_transaction_session_timeout=60s"],
            ignoreOrder: true
        );
    }

    [Fact]
    public async Task The_migrations_history_table_lives_in_the_platform_schema()
    {
        string[] columns = await ScalarAsync<string[]>(
            """
            SELECT array_agg(column_name::text ORDER BY ordinal_position)
            FROM information_schema.columns
            WHERE table_schema = 'platform' AND table_name = '__ef_migrations_history'
            """
        );

        columns.ShouldBe(["migration_id", "product_version"]);
    }

    [Fact]
    public async Task Nobody_can_create_objects_in_the_public_schema()
    {
        bool publicCanCreate = await ScalarAsync<bool>(
            "SELECT has_schema_privilege('public', 'public', 'CREATE')"
        );

        publicCanCreate.ShouldBeFalse();
    }

    [Fact]
    public async Task The_app_role_can_use_but_not_create_in_the_platform_schema()
    {
        bool[] privileges = await ScalarAsync<bool[]>(
            """
            SELECT ARRAY[
                has_schema_privilege('frappe_app', 'platform', 'USAGE'),
                has_schema_privilege('frappe_app', 'platform', 'CREATE')]
            """
        );

        privileges.ShouldBe([true, false]);
    }

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
