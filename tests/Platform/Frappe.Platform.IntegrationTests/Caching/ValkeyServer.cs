using Frappe.Testing.Containers;
using StackExchange.Redis;
using Testcontainers.Redis;
using TUnit.Core.Interfaces;

namespace Frappe.Platform.IntegrationTests.Caching;

/// <summary>
/// One real Valkey per test session. Tests isolate by instance name (the cache's key prefix), so a reused server needs no
/// reset. <see cref="Inspector"/> reads the server directly, the way an operator would, to assert what was stored.
/// </summary>
public sealed class ValkeyServer : IAsyncInitializer, IAsyncDisposable
{
    /// <summary>A loopback port nothing listens on: every connection to it is refused at once.</summary>
    public const string Unreachable = "127.0.0.1:1";

    private readonly RedisContainer _container = new RedisBuilder("valkey/valkey:8.1.11-alpine").ReusedAs("valkey").Build();
    private ConnectionMultiplexer? _inspector;

    /// <summary>The server's connection string, <c>host:port</c>.</summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <summary>A connection of the test itself, apart from the code under test.</summary>
    public IDatabase Inspector => (_inspector ?? throw new InvalidOperationException("The Valkey server has not started.")).GetDatabase();

    /// <summary>The names of the clients connected right now (<c>CLIENT LIST</c>).</summary>
    public async Task<IReadOnlyList<string>> ClientNamesAsync()
    {
        var server = _inspector!.GetServer(_inspector.GetEndPoints()[0]);
        var clients = await server.ClientListAsync();
        return [.. clients.Select(client => client.Name ?? string.Empty)];
    }

    /// <summary>The keys stored right now that match <paramref name="pattern"/> (<c>SCAN MATCH</c>).</summary>
    public async Task<IReadOnlyList<string>> KeysAsync(string pattern)
    {
        var server = _inspector!.GetServer(_inspector.GetEndPoints()[0]);
        var keys = new List<string>();
        await foreach (var key in server.KeysAsync(pattern: pattern))
        {
            keys.Add(key.ToString());
        }

        return keys;
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _inspector = await ConnectionMultiplexer.ConnectAsync($"{ConnectionString},allowAdmin=true");
    }

    public async ValueTask DisposeAsync()
    {
        if (_inspector is not null)
        {
            await _inspector.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}
