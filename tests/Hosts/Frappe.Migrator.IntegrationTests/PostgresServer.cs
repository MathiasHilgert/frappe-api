using Frappe.Testing.Containers;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Frappe.Migrator.IntegrationTests;

/// <summary>One real Postgres per test session, reached as its superuser: what the Migrator's owner role sees.</summary>
public sealed class PostgresServer : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18-alpine").ReusedAs("migrator-postgres").Build();

    /// <summary>The superuser's connection string.</summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await TestReuse.ResetPostgresAsync(ConnectionString);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
