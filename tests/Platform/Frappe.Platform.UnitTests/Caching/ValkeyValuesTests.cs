using Frappe.Caching;
using StackExchange.Redis;

namespace Frappe.Platform.UnitTests.Caching;

/// <summary>The values' arguments and connection against a fake connection attempt: no Valkey.</summary>
public sealed class ValkeyValuesTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);
    private static readonly byte[] Answer = "201 Created"u8.ToArray();

    /// <summary>Counts every connection attempt; it fails each one, or never answers when <paramref name="hangs"/>.</summary>
    private sealed class OpeningProbe(bool hangs = false)
    {
        public int Attempts { get; private set; }

        public ValkeyValues Create() => new(
            () =>
            {
                Attempts++;
                return hangs
                    ? new TaskCompletionSource<ConnectionMultiplexer>().Task
                    : Task.FromException<ConnectionMultiplexer>(
                        new RedisConnectionException(ConnectionFailureType.UnableToConnect, $"attempt {Attempts}"));
            },
            "test");
    }

    [Test]
    public async Task Every_call_checks_its_arguments_and_opens_no_connection()
    {
        // Arrange
        var probe = new OpeningProbe(hangs: true);
        await using var values = probe.Create();

        // Act
        var addEmptyKey = await Assert.ThrowsAsync<ArgumentException>(async () => await values.TryAddAsync(string.Empty, Answer, Lifetime, CancellationToken.None));
        var addZeroLifetime = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await values.TryAddAsync("k", Answer, TimeSpan.Zero, CancellationToken.None));
        var addNoValue = await Assert.ThrowsAsync<ArgumentNullException>(async () => await values.TryAddAsync("k", null!, Lifetime, CancellationToken.None));
        var setEmptyKey = await Assert.ThrowsAsync<ArgumentException>(async () => await values.SetAsync(string.Empty, Answer, Lifetime, CancellationToken.None));
        var setZeroLifetime = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await values.SetAsync("k", Answer, TimeSpan.Zero, CancellationToken.None));
        var setNoValue = await Assert.ThrowsAsync<ArgumentNullException>(async () => await values.SetAsync("k", null!, Lifetime, CancellationToken.None));
        var getEmptyKey = await Assert.ThrowsAsync<ArgumentException>(async () => await values.GetAsync(string.Empty, CancellationToken.None));
        var deleteEmptyKey = await Assert.ThrowsAsync<ArgumentException>(async () => await values.DeleteAsync(string.Empty, CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(addEmptyKey!.ParamName).IsEqualTo("key");
            await Assert.That(addZeroLifetime!.ParamName).IsEqualTo("lifetime");
            await Assert.That(addNoValue!.ParamName).IsEqualTo("value");
            await Assert.That(setEmptyKey!.ParamName).IsEqualTo("key");
            await Assert.That(setZeroLifetime!.ParamName).IsEqualTo("lifetime");
            await Assert.That(setNoValue!.ParamName).IsEqualTo("value");
            await Assert.That(getEmptyKey!.ParamName).IsEqualTo("key");
            await Assert.That(deleteEmptyKey!.ParamName).IsEqualTo("key");
            await Assert.That(probe.Attempts).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Every_call_shares_the_connection_and_retries_a_failed_attempt()
    {
        // Arrange
        var probe = new OpeningProbe();
        await using var values = probe.Create();

        // Act
        var adding = await Assert.ThrowsAsync<RedisConnectionException>(async () => await values.TryAddAsync("k", Answer, Lifetime, CancellationToken.None));
        var setting = await Assert.ThrowsAsync<RedisConnectionException>(async () => await values.SetAsync("k", Answer, Lifetime, CancellationToken.None));
        var getting = await Assert.ThrowsAsync<RedisConnectionException>(async () => await values.GetAsync("k", CancellationToken.None));
        var deleting = await Assert.ThrowsAsync<RedisConnectionException>(async () => await values.DeleteAsync("k", CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(adding!.Message).IsEqualTo("attempt 1");
            await Assert.That(setting!.Message).IsEqualTo("attempt 2");
            await Assert.That(getting!.Message).IsEqualTo("attempt 3");
            await Assert.That(deleting!.Message).IsEqualTo("attempt 4");
            await Assert.That(probe.Attempts).IsEqualTo(4);
        }
    }

    [Test]
    public async Task A_call_after_disposal_opens_no_connection()
    {
        // Arrange
        var probe = new OpeningProbe(hangs: true);
        var values = probe.Create();
        await values.DisposeAsync();

        // Act
        var refused = await Assert.ThrowsAsync<ObjectDisposedException>(async () => await values.GetAsync("k", CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(refused).IsNotNull();
            await Assert.That(probe.Attempts).IsEqualTo(0);
        }
    }
}
