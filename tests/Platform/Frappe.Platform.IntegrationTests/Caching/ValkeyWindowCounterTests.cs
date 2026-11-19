using Frappe.Caching;
using Frappe.Testing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Frappe.Platform.IntegrationTests.Caching;

/// <summary>Fixed window counters on a real Valkey: hits, permits taken up to a limit, permits given back, and windows closing.</summary>
[ClassDataSource<ValkeyServer>(Shared = SharedType.PerTestSession)]
public sealed class ValkeyWindowCounterTests(ValkeyServer server)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    [Test]
    public async Task Hits_add_up_in_one_window_that_later_hits_never_extend()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var provider = CachingHost.Build(server.ConnectionString, instance);
        var counter = provider.GetRequiredService<ValkeyWindowCounter>();
        var first = await counter.HitAsync("anonymous:client", 1, Window, CancellationToken.None);

        // Act
        var second = await counter.HitAsync("anonymous:client", 2, Window, CancellationToken.None);

        // Assert
        var stored = await server.Inspector.StringGetAsync($"{instance}:rate-limit:anonymous:client");
        using (Assert.Multiple())
        {
            await Assert.That(first.Count).IsEqualTo(1);
            await Assert.That(second.Count).IsEqualTo(3);
            await Assert.That(first.Remaining).IsGreaterThan(TimeSpan.Zero).And.IsLessThanOrEqualTo(Window);
            await Assert.That(second.Remaining).IsGreaterThan(TimeSpan.Zero).And.IsLessThanOrEqualTo(first.Remaining);
            await Assert.That((long)stored).IsEqualTo(3);
        }
    }

    [Test]
    public async Task A_window_left_without_an_expiry_gets_one_on_the_next_hit()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var provider = CachingHost.Build(server.ConnectionString, instance);
        var counter = provider.GetRequiredService<ValkeyWindowCounter>();
        await server.Inspector.StringSetAsync($"{instance}:rate-limit:anonymous:client", 5);

        // Act
        var hit = await counter.HitAsync("anonymous:client", 1, Window, CancellationToken.None);

        // Assert
        var expiry = await server.Inspector.KeyTimeToLiveAsync($"{instance}:rate-limit:anonymous:client");
        using (Assert.Multiple())
        {
            await Assert.That(hit.Count).IsEqualTo(6);
            await Assert.That(hit.Remaining).IsEqualTo(Window);
            await Assert.That(expiry).IsNotNull();
        }
    }

    [Test]
    public async Task A_window_closes_on_its_own_and_counting_starts_again()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var provider = CachingHost.Build(server.ConnectionString, instance);
        var counter = provider.GetRequiredService<ValkeyWindowCounter>();
        await counter.HitAsync("anonymous:client", 4, TimeSpan.FromMilliseconds(200), CancellationToken.None);
        await WaitUntil.TrueAsync(
            async () => !await server.Inspector.KeyExistsAsync($"{instance}:rate-limit:anonymous:client"),
            "the window never closed");

        // Act
        var hit = await counter.HitAsync("anonymous:client", 1, Window, CancellationToken.None);

        // Assert
        await Assert.That(hit.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Permits_are_taken_until_the_limit_is_reached_and_then_refused()
    {
        // Arrange
        await using var provider = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName());
        var counter = provider.GetRequiredService<ValkeyWindowCounter>();
        var first = await counter.TryTakeAsync("login:client", 2, Window, CancellationToken.None);
        var second = await counter.TryTakeAsync("login:client", 2, Window, CancellationToken.None);

        // Act
        var refused = await counter.TryTakeAsync("login:client", 2, Window, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first).IsEqualTo(new WindowTake(true, 1, first.Remaining));
            await Assert.That(second).IsEqualTo(new WindowTake(true, 2, second.Remaining));
            await Assert.That(refused.Taken).IsFalse();
            await Assert.That(refused.Count).IsEqualTo(2);
            await Assert.That(refused.Remaining).IsGreaterThan(TimeSpan.Zero).And.IsLessThanOrEqualTo(Window);
        }
    }

    [Test]
    public async Task Callers_taking_at_once_never_take_more_than_the_limit_between_them()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var provider = CachingHost.Build(server.ConnectionString, instance);
        var counter = provider.GetRequiredService<ValkeyWindowCounter>();

        // Act
        var takes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => counter.TryTakeAsync("login:client", 5, Window, CancellationToken.None)));

        // Assert
        var stored = await server.Inspector.StringGetAsync($"{instance}:rate-limit:login:client");
        using (Assert.Multiple())
        {
            await Assert.That(takes.Count(take => take.Taken)).IsEqualTo(5);
            await Assert.That((long)stored).IsEqualTo(5);
        }
    }

    [Test]
    public async Task A_permit_given_back_can_be_taken_again()
    {
        // Arrange
        await using var provider = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName());
        var counter = provider.GetRequiredService<ValkeyWindowCounter>();
        await counter.TryTakeAsync("login:client", 1, Window, CancellationToken.None);
        var left = await counter.ReturnAsync("login:client", CancellationToken.None);

        // Act
        var taken = await counter.TryTakeAsync("login:client", 1, Window, CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(left).IsEqualTo(0);
            await Assert.That(taken.Taken).IsTrue();
            await Assert.That(taken.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Giving_back_more_permits_than_were_taken_never_goes_below_zero()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var provider = CachingHost.Build(server.ConnectionString, instance);
        var counter = provider.GetRequiredService<ValkeyWindowCounter>();
        await counter.TryTakeAsync("login:client", 3, Window, CancellationToken.None);
        await counter.ReturnAsync("login:client", CancellationToken.None);

        // Act
        var left = await counter.ReturnAsync("login:client", CancellationToken.None);

        // Assert
        var stored = await server.Inspector.StringGetAsync($"{instance}:rate-limit:login:client");
        using (Assert.Multiple())
        {
            await Assert.That(left).IsEqualTo(0);
            await Assert.That((long)stored).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Taking_from_several_windows_takes_one_permit_from_each()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var provider = CachingHost.Build(server.ConnectionString, instance);
        var counter = provider.GetRequiredService<ValkeyWindowCounter>();

        // Act
        var take = await counter.TryTakeAllAsync([new WindowLimit("login:client", 2), new WindowLimit("login:account", 3)], Window, CancellationToken.None);

        // Assert
        var client = await server.Inspector.StringGetAsync($"{instance}:rate-limit:login:client");
        var account = await server.Inspector.StringGetAsync($"{instance}:rate-limit:login:account");
        using (Assert.Multiple())
        {
            await Assert.That(take.Taken).IsTrue();
            await Assert.That(take.RefusedBy).IsNull();
            await Assert.That(take.Remaining).IsEqualTo(Window);
            await Assert.That((long)client).IsEqualTo(1);
            await Assert.That((long)account).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Taking_from_several_windows_answers_the_time_until_the_first_of_them_closes()
    {
        // Arrange
        await using var provider = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName());
        var counter = provider.GetRequiredService<ValkeyWindowCounter>();
        await counter.TryTakeAsync("login:account", 3, Window, CancellationToken.None);

        // Act
        var take = await counter.TryTakeAllAsync([new WindowLimit("login:client", 2), new WindowLimit("login:account", 3)], TimeSpan.FromHours(1), CancellationToken.None);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(take.Taken).IsTrue();
            await Assert.That(take.Remaining).IsGreaterThan(TimeSpan.Zero).And.IsLessThanOrEqualTo(Window);
        }
    }

    [Test]
    public async Task A_window_at_its_limit_refuses_the_take_naming_itself_and_nothing_is_taken()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var provider = CachingHost.Build(server.ConnectionString, instance);
        var counter = provider.GetRequiredService<ValkeyWindowCounter>();
        await counter.TryTakeAsync("login:account", 1, Window, CancellationToken.None);

        // Act
        var take = await counter.TryTakeAllAsync([new WindowLimit("login:client", 5), new WindowLimit("login:account", 1)], Window, CancellationToken.None);

        // Assert
        var client = await server.Inspector.KeyExistsAsync($"{instance}:rate-limit:login:client");
        var account = await server.Inspector.StringGetAsync($"{instance}:rate-limit:login:account");
        using (Assert.Multiple())
        {
            await Assert.That(take.Taken).IsFalse();
            await Assert.That(take.RefusedBy).IsEqualTo(1);
            await Assert.That(take.Remaining).IsGreaterThan(TimeSpan.Zero).And.IsLessThanOrEqualTo(Window);
            await Assert.That(client).IsFalse();
            await Assert.That((long)account).IsEqualTo(1);
        }
    }

    [Test]
    public async Task An_unreachable_server_fails_a_hit_with_a_connection_error()
    {
        // Arrange
        await using var provider = CachingHost.Build(ValkeyServer.Unreachable, CachingHost.NewInstanceName());
        var counter = provider.GetRequiredService<ValkeyWindowCounter>();

        // Act
        var failure = await Assert.ThrowsAsync<RedisConnectionException>(async () => await counter.HitAsync("anonymous:client", 1, Window, CancellationToken.None));

        // Assert
        await Assert.That(failure!.Message).Contains("UnableToConnect");
    }
}
