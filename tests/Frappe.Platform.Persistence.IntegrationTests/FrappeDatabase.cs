using DotNet.Testcontainers.Configurations;
using Frappe.Migrations;
using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Frappe.Testing;

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
        await using FrappeDbContext db = new MigrationsDbContextFactory().CreateDbContext([]);
        db.Database.SetConnectionString(OwnerConnectionString);
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    private string ConnectAs(string role, string password) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = role,
            Password = password,
        }.ConnectionString;
}
