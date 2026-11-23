using Frappe.Platform.Testing.Messaging;
using Frappe.Testing.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Frappe.Platform.IntegrationTests.Messaging;

/// <summary>One real Postgres per test session (the inbox claims with <c>ON CONFLICT</c>); every test gets its own database.</summary>
public sealed class InboxDatabase : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgis/postgis:18-3.7-alpine").ReusedAs("inbox").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await TestReuse.ResetPostgresAsync(_container.GetConnectionString());
    }

    /// <summary>A fresh database holding an inbox and the effects table, and the options to reach it.</summary>
    public async Task<DbContextOptions<InboxTestContext>> CreateAsync()
    {
        var connection = NewConnectionString();
        var options = new DbContextOptionsBuilder<InboxTestContext>().UseNpgsql(connection, npgsql => npgsql.UseNodaTime()).Options;
        await using var context = new InboxTestContext(options);
        await context.Database.EnsureCreatedAsync();
        return options;
    }

    /// <summary>The connection string of a database name no other test uses; the database is created by whoever connects to it first.</summary>
    public string NewConnectionString() =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "inbox_" + Guid.NewGuid().ToString("N")[..12],
        }.ConnectionString;

    /// <summary>The server's own connection string, a superuser on its default database.</summary>
    public string ServerConnectionString => _container.GetConnectionString();

    /// <summary>Options of a database that was never created, on the same server: every statement fails.</summary>
    public DbContextOptions<InboxTestContext> Unreachable() =>
        new DbContextOptionsBuilder<InboxTestContext>()
            .UseNpgsql(
                new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
                {
                    Database = "missing_" + Guid.NewGuid().ToString("N")[..12],
                }.ConnectionString,
                npgsql => npgsql.UseNodaTime())
            .Options;

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
