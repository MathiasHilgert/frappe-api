using Frappe.Identity.Application.Security;
using Frappe.Identity.Infrastructure.Security;
using Frappe.Platform.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;

namespace Frappe.Identity.Tests.Security;

/// <summary>Valkey is a non-critical dependency, so an outage lets attempts through instead of locking people out.</summary>
public sealed class ValkeyAttemptLimiterTests : IAsyncDisposable
{
    private static readonly AttemptPolicy Policy = new("test", 1, TimeSpan.FromMinutes(1));
    private readonly ServiceProvider _metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly ValkeyConnection _down = new(ConfigurationOptions.Parse(
        "127.0.0.1:1,abortConnect=false,connectTimeout=200,asyncTimeout=200,syncTimeout=200,connectRetry=0"));

    [Fact]
    public async Task ValkeyDown_ReserveAndRecord_FailOpenAndCount()
    {
        using var counted = new CounterListener(IdentityMetrics.AttemptLimiterUnavailable);
        var limiter = Limiter();

        (await limiter.RecordAsync(Policy, "a@example.com", TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();
        (await limiter.ReserveAsync(Policy, "a@example.com", TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();
        await Should.NotThrowAsync(limiter.ResetAsync(Policy, "a@example.com", TestContext.Current.CancellationToken));

        counted.Total.ShouldBe(3);
    }

    [Fact]
    public async Task ValkeyUnreachableWithDefaultClientTimeouts_FailsOpenWithinTheLimiterBudget()
    {
        // Default client settings would wait about five seconds per call; the limiter's own budget must win.
        await using var unreachable = new ValkeyConnection(ConfigurationOptions.Parse("127.0.0.1:1,abortConnect=false"));
        using var counted = new CounterListener(IdentityMetrics.AttemptLimiterUnavailable);
        var limiter = Limiter(unreachable);

        var started = TimeProvider.System.GetTimestamp();
        var decision = await limiter.ReserveAsync(Policy, "a@example.com", TestContext.Current.CancellationToken);

        TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(TimeSpan.FromSeconds(1));
        decision.IsAllowed.ShouldBeTrue();
        counted.Total.ShouldBe(1);
    }

    [Fact]
    public async Task CallerCancels_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(Limiter().ReserveAsync(Policy, "a@example.com", cancelled.Token));
    }

    public async ValueTask DisposeAsync()
    {
        await _down.DisposeAsync();
        await _metrics.DisposeAsync();
    }

    private ValkeyAttemptLimiter Limiter(ValkeyConnection? valkey = null) =>
        new(valkey ?? _down, Options.Create(new AttemptLimiterOptions { Timeout = TimeSpan.FromMilliseconds(250) }), new IdentityMetrics(_metrics.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            NullLogger<ValkeyAttemptLimiter>.Instance);
}
