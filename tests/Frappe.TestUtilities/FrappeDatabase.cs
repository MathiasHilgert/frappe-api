using DotNet.Testcontainers.Configurations;
using Frappe.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Frappe.TestUtilities;

// PostgreSQL as every environment has it: the roles from the init script and every migration applied
// by the owner. Tests connect as the app role.
public sealed class FrappeDatabase : IAsyncLifetime
{
    private const string OwnerPassword = "owner-test-only";
    private const string AppPassword = "app-test-only";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6")
        .WithDatabase("frappe")
        .WithEnvironment("FRAPPE_OWNER_PASSWORD", OwnerPassword)
        .WithEnvironment("FRAPPE_APP_PASSWORD", AppPassword)
        .WithResourceMapping(
            Path.Combine(AppContext.BaseDirectory, "init-roles.sh"),
            "/docker-entrypoint-initdb.d/",
            fileMode: Unix.FileMode755
        )
        .Build();

    public string OwnerConnectionString => ConnectAs("frappe_owner", OwnerPassword);

    public string AppConnectionString => ConnectAs("frappe_app", AppPassword);

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await MigrationsRunner.MigrateAllAsync(
            OwnerConnectionString,
            TestContext.Current.CancellationToken
        );
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    // Freezes the server, as an outage does: open connections hang instead of failing fast.
    public Task PauseAsync() => _postgres.PauseAsync(TestContext.Current.CancellationToken);

    public Task UnpauseAsync() => _postgres.UnpauseAsync(TestContext.Current.CancellationToken);

    private string ConnectAs(string role, string password) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = role,
            Password = password,
        }.ConnectionString;
}
