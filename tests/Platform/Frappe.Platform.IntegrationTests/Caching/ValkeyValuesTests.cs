using Frappe.Caching;
using Frappe.Testing;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Frappe.Platform.IntegrationTests.Caching;

/// <summary>Small values on a real Valkey: added only when absent, overwritten, read, deleted and expiring on their own.</summary>
[ClassDataSource<ValkeyServer>(Shared = SharedType.PerTestSession)]
public sealed class ValkeyValuesTests(ValkeyServer server)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private static readonly byte[] FirstAnswer = "201 Created"u8.ToArray();
    private static readonly byte[] SecondAnswer = "409 Conflict"u8.ToArray();

    [Test]
    public async Task A_value_is_added_under_the_instance_name_with_its_lifetime()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var provider = CachingHost.Build(server.ConnectionString, instance);
        var values = provider.GetRequiredService<ValkeyValues>();

        // Act
        var added = await values.TryAddAsync("idempotency:k1", FirstAnswer, Lifetime, CancellationToken.None);

        // Assert
        var stored = (byte[]?)await server.Inspector.StringGetAsync($"{instance}:idempotency:k1");
        var expiry = await server.Inspector.KeyTimeToLiveAsync($"{instance}:idempotency:k1");
        using (Assert.Multiple())
        {
            await Assert.That(added).IsTrue();
            await Assert.That(stored).IsEquivalentTo(FirstAnswer);
            await Assert.That(expiry).IsNotNull();
            await Assert.That(expiry!.Value).IsGreaterThan(TimeSpan.Zero).And.IsLessThanOrEqualTo(Lifetime);
        }
    }

    [Test]
    public async Task Adding_a_key_that_exists_is_rejected_and_keeps_the_first_value()
    {
        // Arrange
        await using var provider = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName());
        var values = provider.GetRequiredService<ValkeyValues>();
        await values.TryAddAsync("idempotency:k1", FirstAnswer, Lifetime, CancellationToken.None);

        // Act
        var added = await values.TryAddAsync("idempotency:k1", SecondAnswer, Lifetime, CancellationToken.None);

        // Assert
        var read = await values.GetAsync("idempotency:k1", CancellationToken.None);
        using (Assert.Multiple())
        {
            await Assert.That(added).IsFalse();
            await Assert.That(read).IsEquivalentTo(FirstAnswer);
        }
    }

    [Test]
    public async Task Setting_a_value_replaces_what_was_there()
    {
        // Arrange
        var instance = CachingHost.NewInstanceName();
        await using var provider = CachingHost.Build(server.ConnectionString, instance);
        var values = provider.GetRequiredService<ValkeyValues>();
        await values.TryAddAsync("idempotency:k1", FirstAnswer, Lifetime, CancellationToken.None);

        // Act
        await values.SetAsync("idempotency:k1", SecondAnswer, Lifetime, CancellationToken.None);

        // Assert
        var read = await values.GetAsync("idempotency:k1", CancellationToken.None);
        var expiry = await server.Inspector.KeyTimeToLiveAsync($"{instance}:idempotency:k1");
        using (Assert.Multiple())
        {
            await Assert.That(read).IsEquivalentTo(SecondAnswer);
            await Assert.That(expiry).IsNotNull();
        }
    }

    [Test]
    public async Task Reading_a_key_that_was_never_stored_answers_nothing()
    {
        // Arrange
        await using var provider = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName());
        var values = provider.GetRequiredService<ValkeyValues>();

        // Act
        var read = await values.GetAsync("idempotency:missing", CancellationToken.None);

        // Assert
        await Assert.That(read).IsNull();
    }

    [Test]
    public async Task Deleting_a_value_removes_it_and_a_second_delete_answers_false()
    {
        // Arrange
        await using var provider = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName());
        var values = provider.GetRequiredService<ValkeyValues>();
        await values.TryAddAsync("idempotency:k1", FirstAnswer, Lifetime, CancellationToken.None);

        // Act
        var deleted = await values.DeleteAsync("idempotency:k1", CancellationToken.None);

        // Assert
        var read = await values.GetAsync("idempotency:k1", CancellationToken.None);
        var deletedAgain = await values.DeleteAsync("idempotency:k1", CancellationToken.None);
        using (Assert.Multiple())
        {
            await Assert.That(deleted).IsTrue();
            await Assert.That(read).IsNull();
            await Assert.That(deletedAgain).IsFalse();
        }
    }

    [Test]
    public async Task A_value_expires_on_its_own_after_its_lifetime()
    {
        // Arrange
        await using var provider = CachingHost.Build(server.ConnectionString, CachingHost.NewInstanceName());
        var values = provider.GetRequiredService<ValkeyValues>();
        await values.TryAddAsync("idempotency:k1", FirstAnswer, TimeSpan.FromMilliseconds(200), CancellationToken.None);

        // Act
        await WaitUntil.TrueAsync(async () => await values.GetAsync("idempotency:k1", CancellationToken.None) is null, "the value never expired");

        // Assert
        var added = await values.TryAddAsync("idempotency:k1", SecondAnswer, Lifetime, CancellationToken.None);
        await Assert.That(added).IsTrue();
    }

    [Test]
    public async Task An_unreachable_server_fails_the_call_with_a_connection_error()
    {
        // Arrange
        await using var provider = CachingHost.Build(ValkeyServer.Unreachable, CachingHost.NewInstanceName());
        var values = provider.GetRequiredService<ValkeyValues>();

        // Act
        var failure = await Assert.ThrowsAsync<RedisConnectionException>(async () => await values.GetAsync("idempotency:k1", CancellationToken.None));

        // Assert
        await Assert.That(failure!.Message).Contains("UnableToConnect");
    }
}
