using Frappe.Geo.Infrastructure;
using Frappe.Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;
using TUnit.Core.Interfaces;

namespace Frappe.Geo.IntegrationTests.Persistence;

/// <summary>
/// A migrated Geo database of its own, copied from the session's template: share it per class
/// (<see cref="SharedType.PerClass"/>) or take a fresh one per test (<see cref="SharedType.None"/>).
/// <see cref="Token"/> names everything else the owner must isolate on shared servers (NATS namespace, cache prefix).
/// </summary>
public sealed class GeoDatabase : IAsyncInitializer, IAsyncDisposable
{
    [ClassDataSource<PostgresServer>(Shared = SharedType.PerTestSession)]
    public PostgresServer Server { get; init; } = null!;

    /// <summary>Unique per instance; a valid database name suffix, NATS namespace and cache key prefix.</summary>
    public string Token { get; } = "t" + Guid.NewGuid().ToString("N")[..12];

    private string Name => "geo_" + Token;

    public string ConnectionString => Server.ConnectionStringFor(Name);

    public Task InitializeAsync() => Server.CloneTemplateAsync(Name);

    /// <summary>A context configured exactly like production, including the outbox.</summary>
    public GeoDbContext NewContext(IClock? clock = null) => new(
        GeoPersistenceRegistration.Configure(
                new DbContextOptionsBuilder<GeoDbContext>(),
                ConnectionString,
                clock ?? SystemClock.Instance)
            .Options);

    public async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> read)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    /// <summary>Gives the connections back to the server: idle pools of finished classes would pile up otherwise.</summary>
    public async ValueTask DisposeAsync()
    {
        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            NpgsqlConnection.ClearPool(connection);
        }

        await Server.DropAsync(Name);
    }
}
