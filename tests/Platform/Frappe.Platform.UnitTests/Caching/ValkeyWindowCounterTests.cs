using Frappe.Caching;
using StackExchange.Redis;

namespace Frappe.Platform.UnitTests.Caching;

/// <summary>The counter's connection: a failed attempt is never kept, so a replica gets back to Valkey once it answers.</summary>
public sealed class ValkeyWindowCounterTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Counts every connection attempt; it fails each one, or never answers when <paramref name="hangs"/>.</summary>
    private sealed class OpeningProbe(bool hangs = false)
    {
        public int Attempts { get; private set; }

        public ValkeyWindowCounter Create() => new(
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
    public async Task A_failed_connection_attempt_is_retried_on_the_next_hit()
    {
        // Arrange
        var probe = new OpeningProbe();
        await using var counter = probe.Create();

        // Act
        var first = await Assert.ThrowsAsync<RedisConnectionException>(async () => await counter.HitAsync("anonymous:client", 1, Window, CancellationToken.None));
        var second = await Assert.ThrowsAsync<RedisConnectionException>(async () => await counter.HitAsync("anonymous:client", 1, Window, CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(first!.Message).IsEqualTo("attempt 1");
            await Assert.That(second!.Message).IsEqualTo("attempt 2");
            await Assert.That(probe.Attempts).IsEqualTo(2);
        }
    }

    [Test]
    public async Task Taking_and_returning_share_the_connection_and_retry_a_failed_attempt_like_a_hit()
    {
        // Arrange
        var probe = new OpeningProbe();
        await using var counter = probe.Create();

        // Act
        var taking = await Assert.ThrowsAsync<RedisConnectionException>(async () => await counter.TryTakeAsync("login:client", 10, Window, CancellationToken.None));
        var returning = await Assert.ThrowsAsync<RedisConnectionException>(async () => await counter.ReturnAsync("login:client", CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(taking!.Message).IsEqualTo("attempt 1");
            await Assert.That(returning!.Message).IsEqualTo("attempt 2");
            await Assert.That(probe.Attempts).IsEqualTo(2);
        }
    }

    [Test]
    public async Task Taking_and_returning_check_their_arguments_and_open_no_connection()
    {
        // Arrange
        var probe = new OpeningProbe(hangs: true);
        await using var counter = probe.Create();

        // Act
        var zeroLimit = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await counter.TryTakeAsync("k", 0, Window, CancellationToken.None));
        var zeroWindow = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await counter.TryTakeAsync("k", 1, TimeSpan.Zero, CancellationToken.None));
        var emptyTakeKey = await Assert.ThrowsAsync<ArgumentException>(async () => await counter.TryTakeAsync(string.Empty, 1, Window, CancellationToken.None));
        var emptyReturnKey = await Assert.ThrowsAsync<ArgumentException>(async () => await counter.ReturnAsync(string.Empty, CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(zeroLimit).IsNotNull();
            await Assert.That(zeroWindow).IsNotNull();
            await Assert.That(emptyTakeKey).IsNotNull();
            await Assert.That(emptyReturnKey).IsNotNull();
            await Assert.That(probe.Attempts).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Taking_and_returning_after_disposal_open_no_connection()
    {
        // Arrange
        var probe = new OpeningProbe(hangs: true);
        var counter = probe.Create();
        await counter.DisposeAsync();

        // Act
        var taking = await Assert.ThrowsAsync<ObjectDisposedException>(async () => await counter.TryTakeAsync("k", 1, Window, CancellationToken.None));
        var returning = await Assert.ThrowsAsync<ObjectDisposedException>(async () => await counter.ReturnAsync("k", CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(taking).IsNotNull();
            await Assert.That(returning).IsNotNull();
            await Assert.That(probe.Attempts).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Taking_from_several_windows_checks_its_arguments_and_opens_no_connection()
    {
        // Arrange
        var probe = new OpeningProbe();
        await using var counter = probe.Create();

        // Act
        var noLimits = await Assert.ThrowsAsync<ArgumentException>(async () => await counter.TryTakeAllAsync([], Window, CancellationToken.None));
        var zeroLimit = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await counter.TryTakeAllAsync([new WindowLimit("k", 0)], Window, CancellationToken.None));
        var emptyKey = await Assert.ThrowsAsync<ArgumentException>(async () => await counter.TryTakeAllAsync([new WindowLimit(string.Empty, 1)], Window, CancellationToken.None));
        var zeroWindow = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await counter.TryTakeAllAsync([new WindowLimit("k", 1)], TimeSpan.Zero, CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(noLimits).IsNotNull();
            await Assert.That(zeroLimit).IsNotNull();
            await Assert.That(emptyKey).IsNotNull();
            await Assert.That(zeroWindow).IsNotNull();
            await Assert.That(probe.Attempts).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Taking_from_several_windows_refuses_a_key_named_twice_and_opens_no_connection()
    {
        // Arrange
        var probe = new OpeningProbe();
        await using var counter = probe.Create();

        // Act
        var duplicate = await Assert.ThrowsAsync<ArgumentException>(async () => await counter.TryTakeAllAsync(
            [new WindowLimit("anonymous:client", 5), new WindowLimit("route:orders", 2), new WindowLimit("anonymous:client", 1)],
            Window,
            CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(duplicate!.ParamName).IsEqualTo("limits");
            await Assert.That(duplicate.Message).Contains("anonymous:client");
            await Assert.That(probe.Attempts).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Taking_from_several_windows_shares_the_connection_and_retries_a_failed_attempt()
    {
        // Arrange
        var probe = new OpeningProbe();
        await using var counter = probe.Create();

        // Act
        var several = await Assert.ThrowsAsync<RedisConnectionException>(async () => await counter.TryTakeAllAsync([new WindowLimit("a", 1), new WindowLimit("b", 2)], Window, CancellationToken.None));
        var single = await Assert.ThrowsAsync<RedisConnectionException>(async () => await counter.TryTakeAsync("a", 1, Window, CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(several!.Message).IsEqualTo("attempt 1");
            await Assert.That(single!.Message).IsEqualTo("attempt 2");
            await Assert.That(probe.Attempts).IsEqualTo(2);
        }
    }

    [Test]
    public async Task Taking_from_several_windows_after_disposal_opens_no_connection()
    {
        // Arrange
        var probe = new OpeningProbe();
        var counter = probe.Create();
        await counter.DisposeAsync();

        // Act
        var failure = await Assert.ThrowsAsync<ObjectDisposedException>(async () => await counter.TryTakeAllAsync([new WindowLimit("a", 1)], Window, CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(probe.Attempts).IsEqualTo(0);
        }
    }

    [Test]
    public async Task A_hit_after_disposal_opens_no_connection()
    {
        // Arrange
        var probe = new OpeningProbe();
        var counter = probe.Create();
        await counter.DisposeAsync();

        // Act
        var failure = await Assert.ThrowsAsync<ObjectDisposedException>(async () => await counter.HitAsync("anonymous:client", 1, Window, CancellationToken.None));

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNotNull();
            await Assert.That(probe.Attempts).IsEqualTo(0);
        }
    }

    [Test]
    public async Task Disposal_waits_for_an_attempt_still_opening_and_ignores_its_failure()
    {
        // Arrange
        var opening = new TaskCompletionSource<ConnectionMultiplexer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var counter = new ValkeyWindowCounter(() => opening.Task, "test");
        using var abandon = new CancellationTokenSource();
        var hit = counter.HitAsync("anonymous:client", 1, Window, abandon.Token);
        await abandon.CancelAsync();
        var abandoned = await Assert.ThrowsAsync<OperationCanceledException>(async () => await hit);

        // Act
        var disposal = counter.DisposeAsync().AsTask();
        var waited = !disposal.IsCompleted;
        opening.SetException(new InvalidOperationException("not a Redis failure"));
        var disposalFailure = await Caught.ExceptionOfAsync(async () => await disposal);

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(abandoned).IsNotNull();
            await Assert.That(disposalFailure).IsNull();
            await Assert.That(waited).IsTrue();
        }
    }

    [Test]
    public async Task Disposing_a_counter_that_never_connected_does_nothing()
    {
        // Arrange
        var probe = new OpeningProbe(hangs: true);
        var counter = probe.Create();

        // Act
        var failure = await Caught.ExceptionOfAsync(async () => await counter.DisposeAsync());

        // Assert
        using (Assert.Multiple())
        {
            await Assert.That(failure).IsNull();
            await Assert.That(probe.Attempts).IsEqualTo(0);
        }
    }
}
