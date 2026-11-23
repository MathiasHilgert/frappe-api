using Frappe.Caching;
using Frappe.Testing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Frappe.Platform.IntegrationTests.Caching;

/// <summary>Markers on a real Valkey: opened only when absent, closed only when present, expiring on their own.</summary>
[ClassDataSource<ValkeyServer>(Shared = SharedType.PerTestSession)]
public sealed class ValkeyMarkersTests(ValkeyServer server)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    [Test]
    public async Task A_marker_opens_once_under_the_instance_name_and_a_second_open_is_rejected()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var provider = CachingHost.Build(server.ConnectionString, instance);
        var markers = provider.GetRequiredService<ValkeyMarkers>();
        var opened = await markers.TryOpenAsync("sync:orders", Lifetime, CancellationToken.None);

        // Act
        var reopened = await markers.TryOpenAsync("sync:orders", Lifetime, CancellationToken.None);

        // Assert
        var expiry = await server.Inspector.KeyTimeToLiveAsync($"{instance}:marker:sync:orders");
        using (Assert.Multiple())
        {
            await Assert.That(opened).IsTrue();
            await Assert.That(reopened).IsFalse();
            await Assert.That(expiry).IsNotNull();
            await Assert.That(expiry!.Value).IsGreaterThan(TimeSpan.Zero).And.IsLessThanOrEqualTo(Lifetime);
        }
    }

    [Test]
    public async Task Closing_an_open_marker_removes_it_so_it_opens_again()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var provider = CachingHost.Build(server.ConnectionString, instance);
        var markers = provider.GetRequiredService<ValkeyMarkers>();
        await markers.TryOpenAsync("sync:orders", Lifetime, CancellationToken.None);

        // Act
        var closed = await markers.TryCloseAsync("sync:orders", CancellationToken.None);

        // Assert
        var stored = await server.Inspector.KeyExistsAsync($"{instance}:marker:sync:orders");
        var reopened = await markers.TryOpenAsync("sync:orders", Lifetime, CancellationToken.None);
        using (Assert.Multiple())
        {
            await Assert.That(closed).IsTrue();
            await Assert.That(stored).IsFalse();
            await Assert.That(reopened).IsTrue();
        }
    }

    [Test]
    public async Task Closing_a_marker_that_is_not_open_answers_false()
    {
        // Arrange
        await using var provider = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName());
        var markers = provider.GetRequiredService<ValkeyMarkers>();

        // Act
        var closed = await markers.TryCloseAsync("sync:never-opened", CancellationToken.None);

        // Assert
        await Assert.That(closed).IsFalse();
    }

    [Test]
    public async Task A_marker_expires_on_its_own_after_its_lifetime()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var provider = CachingHost.Build(server.ConnectionString, instance);
        var markers = provider.GetRequiredService<ValkeyMarkers>();
        await markers.TryOpenAsync("sync:orders", TimeSpan.FromMilliseconds(200), CancellationToken.None);

        // Act
        await WaitUntil.TrueAsync(
            async () => !await server.Inspector.KeyExistsAsync($"{instance}:marker:sync:orders"),
            "the marker never expired");

        // Assert
        var reopened = await markers.TryOpenAsync("sync:orders", Lifetime, CancellationToken.None);
        await Assert.That(reopened).IsTrue();
    }

    [Test]
    public async Task Markers_of_another_instance_name_on_the_same_server_are_apart()
    {
        // Arrange
        await using var first = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName());
        await using var second = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName());
        await first.GetRequiredService<ValkeyMarkers>().TryOpenAsync("sync:orders", Lifetime, CancellationToken.None);

        // Act
        var opened = await second.GetRequiredService<ValkeyMarkers>().TryOpenAsync("sync:orders", Lifetime, CancellationToken.None);

        // Assert
        await Assert.That(opened).IsTrue();
    }

    [Test]
    public async Task Disposing_the_markers_closes_their_connection_and_refuses_further_calls()
    {
        // Arrange
        var client = $"markers-{Guid.NewGuid():N}";
        var provider = CachingHost.Build($"{server.ConnectionString},name={client}", CachingHost.NewInstanceName());
        var markers = provider.GetRequiredService<ValkeyMarkers>();
        await markers.TryOpenAsync("sync:orders", Lifetime, CancellationToken.None);
        var connectedBefore = (await server.ClientNamesAsync()).Contains(client);

        // Act
        await provider.DisposeAsync();

        // Assert
        await WaitUntil.TrueAsync(async () => !(await server.ClientNamesAsync()).Contains(client), "the markers' connection was never closed");
        var refused = await Assert.ThrowsAsync<ObjectDisposedException>(async () => await markers.TryOpenAsync("sync:orders", Lifetime,
            CancellationToken.None));
        using (Assert.Multiple())
        {
            await Assert.That(connectedBefore).IsTrue();
            await Assert.That(refused).IsNotNull();
        }
    }

    [Test]
    public async Task An_unreachable_server_fails_the_call_with_a_connection_error()
    {
        // Arrange
        await using var provider = CachingHost.Build(ValkeyServer.Unreachable, CachingHost.NewInstanceName());
        var markers = provider.GetRequiredService<ValkeyMarkers>();

        // Act
        var failure = await Assert.ThrowsAsync<RedisConnectionException>(async () => await markers.TryOpenAsync("sync:orders", Lifetime,
            CancellationToken.None));

        // Assert
        await Assert.That(failure!.Message).Contains("UnableToConnect");
    }
}
