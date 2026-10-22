using Alba;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Wolverine;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Frappe.IntegrationTests.ApiFixture))]

namespace Frappe.IntegrationTests;

/// <summary>Boots the real API against a throwaway Postgres container.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public IAlbaHost Host { get; private set; } = null!;

    public string PostgresConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
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
        await Host.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
