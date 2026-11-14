using Testcontainers.Redis;
using TUnit.Core.Interfaces;

namespace Frappe.Geo.IntegrationTests.Hosts;

/// <summary>One real Valkey (Redis protocol) per test session, for the Api's L2 cache and backplane.</summary>
public sealed class ValkeyCache : IAsyncInitializer, IAsyncDisposable
{
    private readonly RedisContainer _container = new RedisBuilder("valkey/valkey:8-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
