using Frappe.Geo.Global.Cldr;
using Frappe.Geo.Infrastructure;
using Frappe.Geo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Frappe.Geo.IntegrationTests.Persistence;

/// <summary>
/// One real Postgres (the same image the compose file uses) per test session, holding a template database migrated
/// once. Every <see cref="GeoDatabase"/> is a fresh copy of the template, so test classes never share state.
/// </summary>
public sealed class PostgresServer : IAsyncInitializer, IAsyncDisposable
{
    private const string Template = "geo_template";

    // Many classes run at once, each with its own pools (and the Worker's scheduler): the default 100 is not enough.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgis/postgis:18-3.7-alpine")
        .WithCommand("-c", "max_connections=500")
        .Build();

    private readonly SemaphoreSlim _cloning = new(1, 1);

    /// <summary>The server's maintenance database: create or drop databases from here.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>A connection string to <paramref name="database"/> on this server.</summary>
    public string ConnectionStringFor(string database, bool pooling = true) =>
        new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database, Pooling = pooling }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await ExecuteAsync($"CREATE DATABASE {Template}");

        // Unpooled: Postgres refuses to copy a template while any connection to it is open.
        var options = GeoPersistenceRegistration.Configure(
            new DbContextOptionsBuilder<GeoDbContext>(), ConnectionStringFor(Template, pooling: false), SystemClock.Instance, new CldrCatalogSeed());
        await using var context = new GeoDbContext(options.Options);
        await context.Database.MigrateAsync();
    }

    /// <summary>Creates <paramref name="database"/> as a copy of the migrated template.</summary>
    public async Task CloneTemplateAsync(string database)
    {
        // One copy at a time: a copy reads the template, and Postgres refuses a second one while the first is running.
        await _cloning.WaitAsync();
        try
        {
            await ExecuteAsync($"CREATE DATABASE \"{database}\" TEMPLATE {Template}");
        }
        finally
        {
            _cloning.Release();
        }
    }

    /// <summary>Drops <paramref name="database"/>, closing whatever connections are still open on it.</summary>
    public Task DropAsync(string database) => ExecuteAsync($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)");

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _cloning.Dispose();
        await _container.DisposeAsync();
    }
}
