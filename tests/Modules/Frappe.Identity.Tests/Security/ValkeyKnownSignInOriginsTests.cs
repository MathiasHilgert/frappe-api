using Frappe.Identity.Infrastructure.Security;
using Frappe.Platform.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using StackExchange.Redis;

namespace Frappe.Identity.Tests.Security;

/// <summary>An unreachable Valkey makes every origin unknown, counted on its own so dashboards tell it apart.</summary>
public sealed class ValkeyKnownSignInOriginsTests : IAsyncDisposable
{
    private readonly ServiceProvider _metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly ValkeyConnection _down = new(ConfigurationOptions.Parse(
        "127.0.0.1:1,abortConnect=false,connectTimeout=200,asyncTimeout=200,syncTimeout=200,connectRetry=0"));

    [Fact]
    public async Task ValkeyDown_OriginsReadAsUnknown_AndCountOnTheirOwnCounter()
    {
        using var origins = new CounterListener(IdentityMetrics.KnownOriginsUnavailableName);
        var store = new ValkeyKnownSignInOrigins(_down, Options.Create(new AttemptLimiterOptions { Timeout = TimeSpan.FromMilliseconds(250) }),
            new IdentityMetrics(_metrics.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()),
            NullLogger<ValkeyKnownSignInOrigins>.Instance);

        (await store.IsKnownAsync("a@example.com", "203.0.113.7", TestContext.Current.CancellationToken)).ShouldBeFalse();
        await Should.NotThrowAsync(store.RememberAsync("a@example.com", "203.0.113.7", TestContext.Current.CancellationToken));

        origins.Total.ShouldBe(2);
    }

    public async ValueTask DisposeAsync()
    {
        await _down.DisposeAsync();
        await _metrics.DisposeAsync();
    }
}
