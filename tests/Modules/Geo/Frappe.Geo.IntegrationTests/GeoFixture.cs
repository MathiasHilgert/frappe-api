using Alba;
using Frappe.Geo.Infrastructure;
using Frappe.Geo.IntegrationTests;
using Frappe.Migrations;
using Frappe.TestUtilities;
using Microsoft.AspNetCore.Builder;

[assembly: AssemblyFixture(typeof(GeoFixture))]

namespace Frappe.Geo.IntegrationTests;

// One migrated and seeded database and one host per test assembly; geo is read-only at runtime, so tests share both.
public sealed class GeoFixture : IAsyncLifetime
{
    private readonly FrappeDatabase _database = new();
    private IAlbaHost? _host;

    public string OwnerConnectionString => _database.OwnerConnectionString;

    public IAlbaHost Host =>
        _host ?? throw new InvalidOperationException("The fixture has not been initialized.");

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        await MigrationsRunner.SeedAllAsync(
            OwnerConnectionString,
            [new GeoModule()],
            TestContext.Current.CancellationToken
        );
        _host = await StartHostAsync();
    }

    // A host of its own, for a test that adds services, such as a command counter.
    public Task<IAlbaHost> StartHostAsync(Action<WebApplicationBuilder>? configure = null) =>
        FrappeTestHost.StartAsync(_database.AppConnectionString, [new GeoModule()], configure);

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        await _database.DisposeAsync();
    }
}
