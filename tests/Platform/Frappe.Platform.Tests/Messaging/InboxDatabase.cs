using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace Frappe.Platform.Tests.Messaging;

/// <summary>One real Postgres per test session (the inbox claims with <c>ON CONFLICT</c>); every test gets its own database.</summary>
public sealed class InboxDatabase : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgis/postgis:18-3.7-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();

    /// <summary>A fresh database holding an inbox and the effects table, and the options to reach it.</summary>
    public async Task<DbContextOptions<InboxTestContext>> CreateAsync()
    {
        var connection = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "inbox_" + Guid.NewGuid().ToString("N")[..12],
        }.ConnectionString;
        var options = new DbContextOptionsBuilder<InboxTestContext>().UseNpgsql(connection, npgsql => npgsql.UseNodaTime()).Options;
        await using var context = new InboxTestContext(options);
        await context.Database.EnsureCreatedAsync();
        return options;
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
