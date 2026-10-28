using Alba;
using Frappe.Platform.Persistence.IntegrationTests;
using Frappe.TestUtilities;
using Xunit.Sdk;
using Xunit.v3;

[assembly: AssemblyFixture(typeof(AppFixture))]

// Workaround for dotnet/aspnetcore#69684: when Program fails to start, WebApplicationFactory can throw
// ObjectDisposedException instead of the startup error if the app thread disposes the host before the
// test thread reads it. Parallel host boots starve the test thread enough to lose that race often;
// running classes one at a time narrows it. Remove once the fix (dotnet/aspnetcore#69685) ships.
[assembly: Parallelization(Mode = ParallelMode.None)]

namespace Frappe.Platform.Persistence.IntegrationTests;

// One migrated database and one host per test assembly; tests only read, so they share both.
public sealed class AppFixture : IAsyncLifetime
{
    private readonly FrappeDatabase _database = new();
    private IAlbaHost? _host;

    // The app role, as the host connects.
    public string ConnectionString => _database.AppConnectionString;

    public string OwnerConnectionString => _database.OwnerConnectionString;

    public IAlbaHost Host =>
        _host ?? throw new InvalidOperationException("The fixture has not been initialized.");

    public async ValueTask InitializeAsync()
    {
        await _database.InitializeAsync();
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

        await _database.DisposeAsync();
    }
}
