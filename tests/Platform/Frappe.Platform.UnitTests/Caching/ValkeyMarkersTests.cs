using Frappe.Caching;
using StackExchange.Redis;

namespace Frappe.Platform.UnitTests.Caching;

/// <summary>The markers' arguments and connection against a fake connection attempt: no Valkey.</summary>
public sealed class ValkeyMarkersTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    /// <summary>Counts every connection attempt; it fails each one, or never answers when <paramref name="hangs"/>.</summary>
    private sealed class OpeningProbe(bool hangs = false)
    {
        public int Attempts { get; private set; }

        public ValkeyMarkers Create() => new(
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
    public async Task Opening_and_closing_check_their_arguments_and_open_no_connection()
    {
        // Arrange
        var probe = new OpeningProbe(hangs: true);
        await using var markers = probe.Create();

        // Act
        var emptyOpenKey = await Assert.ThrowsAsync<ArgumentException>(async () => await markers.TryOpenAsync(string.Empty, Lifetime, CancellationToken.None));
        var zeroLifetime = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await markers.TryOpenAsync("k", TimeSpan.Zero, CancellationToken.None));
        var emptyCloseKey = await Assert.ThrowsAsync<ArgumentException>(async () => await markers.TryCloseAsync(string.Empty, CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(emptyOpenKey!.ParamName).IsEqualTo("key");
            await Assert.That(zeroLifetime!.ParamName).IsEqualTo("lifetime");
            await Assert.That(emptyCloseKey!.ParamName).IsEqualTo("key");
            await Assert.That(probe.Attempts).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_failed_connection_attempt_is_retried_on_the_next_call()
    {
        // Arrange
        var probe = new OpeningProbe();
        await using var markers = probe.Create();

        // Act
        var opening = await Assert.ThrowsAsync<RedisConnectionException>(async () => await markers.TryOpenAsync("k", Lifetime, CancellationToken.None));
        var closing = await Assert.ThrowsAsync<RedisConnectionException>(async () => await markers.TryCloseAsync("k", CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(opening!.Message).IsEqualTo("attempt 1");
            await Assert.That(closing!.Message).IsEqualTo("attempt 2");
            await Assert.That(probe.Attempts).IsEqualTo(2);
        }
    }

    [Test]
    public async Task Disposal_after_a_failed_attempt_closes_nothing_and_refuses_further_calls()
    {
        // Arrange
        var probe = new OpeningProbe();
        var markers = probe.Create();
        await Assert.ThrowsAsync<RedisConnectionException>(async () => await markers.TryOpenAsync("k", Lifetime, CancellationToken.None));

        // Act
        await markers.DisposeAsync();

        // Assert
        var refused = await Assert.ThrowsAsync<ObjectDisposedException>(async () => await markers.TryCloseAsync("k", CancellationToken.None));
        using (Assert.Multiple())
        {
            await Assert.That(refused).IsNotNull();
            await Assert.That(probe.Attempts).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Disposal_of_markers_that_never_connected_opens_no_connection()
    {
        // Arrange
        var probe = new OpeningProbe(hangs: true);
        var markers = probe.Create();

        // Act
        await markers.DisposeAsync();

        // Assert
        var refused = await Assert.ThrowsAsync<ObjectDisposedException>(async () => await markers.TryOpenAsync("k", Lifetime, CancellationToken.None));
        using (Assert.Multiple())
        {
            await Assert.That(refused).IsNotNull();
            await Assert.That(probe.Attempts).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Disposal_waits_for_an_attempt_still_opening_and_ignores_its_failure()
    {
        // Arrange
        var opening = new TaskCompletionSource<ConnectionMultiplexer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var markers = new ValkeyMarkers(() => opening.Task, "test");
        using var abandon = new CancellationTokenSource();
        var call = markers.TryOpenAsync("k", Lifetime, abandon.Token);
        await abandon.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await call);
        var disposal = markers.DisposeAsync().AsTask();
        var waited = !disposal.IsCompleted;

        // Act
        opening.SetException(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "refused while stopping"));

        // Assert
        await disposal;
        using (Assert.Multiple())
        {
            await Assert.That(waited).IsTrue();
            await Assert.That(disposal.IsCompletedSuccessfully).IsTrue();
        }
    }
}
