using Alba;
using Frappe.Geo.Infrastructure;
using Frappe.Geo.IntegrationTests;
using Frappe.TestUtilities;

[assembly: AssemblyFixture(typeof(AppFixture))]

namespace Frappe.Geo.IntegrationTests;

// One migrated and seeded database, one NATS server and one host running the geo module per test assembly; the
// catalogues are global and the tests only read them, so they share all three.
public sealed class AppFixture : IAsyncLifetime
{
    private readonly FrappeDatabase _database = new();
    private IAlbaHost? _host;

    public FrappeNats Nats { get; } = new();

    public string OwnerConnectionString => _database.OwnerConnectionString;

    public string AppConnectionString => _database.AppConnectionString;

    public IAlbaHost Host =>
        _host ?? throw new InvalidOperationException("The fixture has not been initialized.");

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        await Nats.InitializeAsync();
        _host = await FrappeTestHost.StartAsync(
            _database.AppConnectionString,
            [new GeoModule()],
            nats: NatsScope.On(Nats)
        );
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.DisposeAsync();
        }

        await Nats.DisposeAsync();
        await _database.DisposeAsync();
    }
}
