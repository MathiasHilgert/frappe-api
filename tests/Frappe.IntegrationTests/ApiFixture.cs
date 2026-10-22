using Alba;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(Frappe.IntegrationTests.ApiFixture))]

namespace Frappe.IntegrationTests;

/// <summary>Boots the real API against a throwaway Postgres and Valkey containers.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly RedisContainer _valkey = new RedisBuilder("valkey/valkey:8-alpine").Build();

    public IAlbaHost Host { get; private set; } = null!;

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _valkey.StartAsync());
        Host = await CreateHostAsync();
    }

    /// <summary>Boots an extra host on the same container, e.g. with test-only services or another database.</summary>
    public Task<IAlbaHost> CreateHostAsync(
        Action<IServiceCollection>? configureServices = null,
        string environment = "Development",
        string? connectionString = null) =>
        AlbaHost.For<Program>(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:frappe", connectionString ?? _postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:valkey", _valkey.GetConnectionString());
            builder.ConfigureServices(services =>
            {
                services.RunWolverineInSoloMode();
                configureServices?.Invoke(services);
            });
        });

    /// <summary>Creates an empty database on the container, for tests that must start without any schema.</summary>
    public async Task<string> CreateDatabaseAsync(string name)
    {
        await using (var dataSource = NpgsqlDataSource.Create(PostgresConnectionString))
        await using (var command = dataSource.CreateCommand($"CREATE DATABASE \"{name}\""))
            await command.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(PostgresConnectionString) { Database = name }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        // Host stays unset when startup failed; disposing must not mask that error.
        if (Host is not null)
            await Host.DisposeAsync();

        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _valkey.DisposeAsync().AsTask());
    }
}
