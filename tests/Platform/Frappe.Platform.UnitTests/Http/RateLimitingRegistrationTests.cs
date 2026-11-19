using Frappe.Caching;
using Frappe.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.UnitTests.Http;

/// <summary>Which <see cref="IRateLimitStore"/> a host gets: its own, Valkey when caching is registered, else the process's memory.</summary>
public sealed class RateLimitingRegistrationTests
{
    /// <summary>A store a host brings.</summary>
    private sealed class HostStore : IRateLimitStore
    {
        public ValueTask<RateLimitWindow> HitAsync(string key, int permits, TimeSpan window, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new RateLimitWindow(permits, window));
    }

    private static IRateLimitStore StoreOf(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);
        services.AddFrappeHttp();
        return services.BuildServiceProvider().GetRequiredService<IRateLimitStore>();
    }

    [Test]
    public async Task Without_caching_the_process_counts_in_memory()
    {
        // Act
        var store = StoreOf(_ => { });

        // Assert
        await Assert.That(store).IsTypeOf<InMemoryRateLimitStore>();
    }

    [Test]
    public async Task With_caching_every_replica_counts_in_Valkey()
    {
        // Act
        var store = StoreOf(services => services.AddSingleton(new ValkeyWindowCounter(new StackExchange.Redis.ConfigurationOptions { EndPoints = { "localhost:1" } }, "test")));

        // Assert
        await Assert.That(store).IsTypeOf<ValkeyRateLimitStore>();
    }

    [Test]
    public async Task A_store_the_host_registered_first_is_kept()
    {
        // Act
        var store = StoreOf(services => services.AddSingleton<IRateLimitStore, HostStore>());

        // Assert
        await Assert.That(store).IsTypeOf<HostStore>();
    }
}
