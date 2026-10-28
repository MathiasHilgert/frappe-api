using Frappe.Host.Tests;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace Frappe.Host.Tests;

// Readiness checks the database, so every host under test needs a real one.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();
}
