using Frappe.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Frappe.Platform.IntegrationTests.Persistence.Revisions;

// Creates the basket tables as the owner and lets the app role use them; contexts connect as the app
// role through the host's data source and options, as a module's context would.
public sealed class BasketDatabase(AppFixture app) : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await using BasketContext owner = new(Options(dataSource: null));
        owner.Database.SetConnectionString(app.OwnerConnectionString);
        await owner
            .GetService<IRelationalDatabaseCreator>()
            .CreateTablesAsync(TestContext.Current.CancellationToken);
        await owner.Database.ExecuteSqlRawAsync(
            $"""
            GRANT USAGE ON SCHEMA {BasketContext.Schema} TO frappe_app;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {BasketContext.Schema} TO frappe_app;
            """,
            TestContext.Current.CancellationToken
        );
    }

    public async ValueTask DisposeAsync()
    {
        await using NpgsqlConnection connection = new(app.OwnerConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand drop = new(
            $"DROP SCHEMA {BasketContext.Schema} CASCADE",
            connection
        );
        await drop.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    internal BasketContext CreateContext() =>
        new(Options(app.Host.Services.GetRequiredService<NpgsqlDataSource>()));

    private static DbContextOptions<BasketContext> Options(NpgsqlDataSource? dataSource)
    {
        DbContextOptionsBuilder<BasketContext> options = new();
        options.UseFrappeDatabase(dataSource, BasketContext.Schema);
        return options.Options;
    }
}
