using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Frappe.Testing.Containers;

/// <summary>
/// One real Postgres per test session, reached as its superuser: what the Migrator's owner role sees. Share it with
/// <c>[ClassDataSource&lt;PostgresServer&gt;(Shared = SharedType.PerTestSession)]</c>. The server is tuned for throwaway data
/// (no fsync, no synchronous commit, no full-page writes, data directory on tmpfs); never copy these settings outside tests.
/// </summary>
public sealed class PostgresServer : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgis/postgis:18-3.7-alpine")
        .WithTmpfsMount("/var/lib/postgresql")
        .WithCommand("-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off")
        .ReusedAs("postgres")
        .Build();

    /// <summary>The superuser's connection string.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await TestReuse.ResetPostgresAsync(ConnectionString);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
