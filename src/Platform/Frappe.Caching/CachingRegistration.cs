using System.Text.Json;
using Frappe.Health;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Frappe.Caching;

/// <summary>
/// FusionCache with three layers: memory per process (L1), Valkey shared by every replica (L2), and a Valkey
/// backplane so an invalidation in one process evicts the L1 of all the others. Fail-safe (for <c>GetOrSet</c> reads)
/// keeps serving the last good value for a while if the source fails, and jitter avoids every replica expiring at the
/// same instant. Valkey is optional at runtime: short timeouts and circuit breakers turn an outage into plain cache
/// misses (the database answers) instead of seconds of waiting per read. For the same reason Valkey is an optional
/// readiness dependency: an outage reports <c>Degraded</c> and keeps the replica in traffic.
/// </summary>
public static class CachingRegistration
{
    private static readonly TimeSpan DistributedSoftTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DistributedHardTimeout = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// The cache, bound from <c>ConnectionStrings:Valkey</c> (required) and <c>Caching:InstanceName</c>
    /// (<see cref="CachingOptions"/>). A missing connection string fails at startup naming the key.
    /// </summary>
    public static IServiceCollection AddFrappeCaching(this IServiceCollection services)
    {
        services.AddOptions<CachingOptions>()
            .BindConfiguration(CachingOptions.Section)
            .Configure<IConfiguration>((options, configuration) =>
                options.ConnectionString = configuration.GetConnectionString(CachingOptions.ConnectionStringName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), $"Configuration 'ConnectionStrings:{CachingOptions.ConnectionStringName}' is required.")
            .PostConfigure(options => options.InstanceName = string.IsNullOrWhiteSpace(options.InstanceName) ? CachingOptions.DefaultInstanceName : options.InstanceName)
            .ValidateOnStart();
        return services.AddFrappeCachingServices();
    }

    /// <summary>The same as <see cref="AddFrappeCaching(IServiceCollection)"/>, with the settings given in code.</summary>
    public static IServiceCollection AddFrappeCaching(this IServiceCollection services, string valkeyConnectionString, string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valkeyConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
        services.AddOptions<CachingOptions>().Configure(options =>
        {
            options.ConnectionString = valkeyConnectionString;
            options.InstanceName = instanceName;
        });
        return services.AddFrappeCachingServices();
    }

    private static IServiceCollection AddFrappeCachingServices(this IServiceCollection services)
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web).ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        services.AddFusionCache()
            .WithOptions((provider, options) =>
            {
                var instanceName = Settings(provider).InstanceName;
                options.CacheKeyPrefix = $"{instanceName}:";

                // The backplane channel defaults to the cache name, which every instance shares: caches with different
                // instance names on one Valkey would then hear each other's invalidations.
                options.BackplaneChannelPrefix = instanceName;
                options.DistributedCacheCircuitBreakerDuration = TimeSpan.FromSeconds(30);
                options.BackplaneCircuitBreakerDuration = TimeSpan.FromSeconds(30);

                // Tag expirations are read from L2 on every tagged read, with their own entry options:
                // they need the same short timeouts, or a Valkey outage costs a full timeout per tag per read.
                options.TagsDefaultEntryOptions.DistributedCacheSoftTimeout = DistributedSoftTimeout;
                options.TagsDefaultEntryOptions.DistributedCacheHardTimeout = DistributedHardTimeout;
                options.TagsDefaultEntryOptions.AllowBackgroundDistributedCacheOperations = true;
            })
            .WithDefaultEntryOptions(entry =>
            {
                // The hard bound for any stale copy that slipped past an invalidation.
                entry.Duration = TimeSpan.FromMinutes(10);
                entry.IsFailSafeEnabled = true;
                entry.FailSafeMaxDuration = TimeSpan.FromHours(2);
                entry.FailSafeThrottleDuration = TimeSpan.FromSeconds(30);
                entry.JitterMaxDuration = TimeSpan.FromSeconds(30);
                entry.DistributedCacheSoftTimeout = DistributedSoftTimeout;
                entry.DistributedCacheHardTimeout = DistributedHardTimeout;
                entry.AllowBackgroundDistributedCacheOperations = true;
            })
            .WithSerializer(new FusionCacheSystemTextJsonSerializer(json))
            .WithDistributedCache(provider => new RedisCache(new RedisCacheOptions { ConfigurationOptions = Resilient(provider) }))
            .WithBackplane(provider => new RedisBackplane(new RedisBackplaneOptions { ConfigurationOptions = Resilient(provider) }));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<CacheInvalidator>();
        services.TryAddSingleton(provider => new ValkeyHealthCheck(Resilient(provider)));
        services.AddOptionalDependency("valkey", provider => provider.GetRequiredService<ValkeyHealthCheck>());
        return services;
    }

    private static CachingOptions Settings(IServiceProvider provider) => provider.GetRequiredService<IOptions<CachingOptions>>().Value;

    /// <summary>Never fail the process because Valkey is down, and never wait long for it.</summary>
    private static ConfigurationOptions Resilient(IServiceProvider provider)
    {
        var options = ConfigurationOptions.Parse(Settings(provider).ConnectionString!);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 1000;
        options.SyncTimeout = 1000;
        options.AsyncTimeout = 1000;
        return options;
    }
}
