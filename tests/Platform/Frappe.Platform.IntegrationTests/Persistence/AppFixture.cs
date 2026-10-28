using Alba;
using Frappe.Platform.IntegrationTests.Persistence;
using Frappe.TestUtilities;

// The container starts even when only tests that build their own host (Health) run; one boot per run.
[assembly: AssemblyFixture(typeof(AppFixture))]

namespace Frappe.Platform.IntegrationTests.Persistence;

// One migrated database, one NATS server and one host per test assembly; tests only read the database, so they
// share it, and each messaging host takes a NatsScope of its own on the server.
public sealed class AppFixture : IAsyncLifetime
{
    private readonly FrappeDatabase _database = new();
    private IAlbaHost? _host;

    public FrappeNats Nats { get; } = new();

    // The app role, as the host connects.
    public string ConnectionString => _database.AppConnectionString;

    public string OwnerConnectionString => _database.OwnerConnectionString;

    public string NatsUrl => Nats.Url;

    public IAlbaHost Host =>
        _host ?? throw new InvalidOperationException("The fixture has not been initialized.");

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
        await Nats.InitializeAsync();
        _host = await AlbaHost.For<Program>(builder =>
            builder
                .UseSetting("ConnectionStrings:Frappe", ConnectionString)
                .UseSetting("Nats:Url", NatsUrl)
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
