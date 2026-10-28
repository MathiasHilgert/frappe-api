using Alba;
using Frappe.Platform.Persistence.IntegrationTests;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(AppFixture))]

namespace Frappe.Platform.Persistence.IntegrationTests;

// One PostgreSQL container and one host per test assembly; tests only read, so they share both.
public sealed class AppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.6").Build();
    private IAlbaHost? _host;

    public string ConnectionString => _postgres.GetConnectionString();

    public IAlbaHost Host =>
        _host ?? throw new InvalidOperationException("The fixture has not been initialized.");

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        _host = await AlbaHost.For<Program>(builder =>
            builder.UseSetting("ConnectionStrings:Frappe", ConnectionString)
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
}
