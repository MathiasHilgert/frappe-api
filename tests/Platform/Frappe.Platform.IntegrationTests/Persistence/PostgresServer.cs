using Frappe.Testing.Containers;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Frappe.Platform.IntegrationTests.Persistence;

/// <summary>One real Postgres per test session, reached as its superuser: what the Migrator's owner role sees.</summary>
public sealed class PostgresServer : IAsyncInitializer, IAsyncDisposable
{
    /// <summary>
    /// Room for every test the session runs at once: each holds a few connections (its owner, its roles, the superuser),
    /// and on a machine with many cores the default of 100 runs out before the tests do.
    /// </summary>
    private const string MaxConnections = "max_connections=500";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgis/postgis:18-3.7-alpine")
        .WithCommand("-c", MaxConnections)
        .ReusedAs("postgres")
        .Build();

    /// <summary>The superuser's connection string.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await TestReuse.ResetPostgresAsync(ConnectionString);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
