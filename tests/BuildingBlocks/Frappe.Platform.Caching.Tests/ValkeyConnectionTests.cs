using NSubstitute;
using Shouldly;
using StackExchange.Redis;

namespace Frappe.Platform.Caching.Tests;

public sealed class ValkeyConnectionTests
{
    [Fact]
    public async Task GetAsync_AfterFailedConnect_RetriesOnNextCall()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var attempts = 0;
        await using var connection = new ValkeyConnection(() => ++attempts == 1
            ? Task.FromException<IConnectionMultiplexer>(new TimeoutException("Valkey is down."))
            : Task.FromResult(multiplexer));

        await Should.ThrowAsync<TimeoutException>(connection.GetAsync());

        (await connection.GetAsync()).ShouldBeSameAs(multiplexer);
        attempts.ShouldBe(2);
    }

    [Fact]
    public async Task GetAsync_ConcurrentCallsWhileConnecting_ShareOneAttempt()
    {
        var pending = new TaskCompletionSource<IConnectionMultiplexer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        await using var connection = new ValkeyConnection(() =>
        {
            Interlocked.Increment(ref attempts);
            return pending.Task;
        });

        // StartNew, unlike Task.Run, does not unwrap: each call hands back the connect task it got.
        var calls = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Factory.StartNew(
            connection.GetAsync, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default)));
        pending.SetResult(Substitute.For<IConnectionMultiplexer>());

        calls.Distinct().Count().ShouldBe(1);
        attempts.ShouldBe(1);
    }

    [Fact]
    public async Task GetAsync_AfterSuccessfulConnect_ReusesTheConnection()
    {
        var attempts = 0;
        await using var connection = new ValkeyConnection(() =>
        {
            attempts++;
            return Task.FromResult(Substitute.For<IConnectionMultiplexer>());
        });

        (await connection.GetAsync()).ShouldBeSameAs(await connection.GetAsync());
        attempts.ShouldBe(1);
    }
}
