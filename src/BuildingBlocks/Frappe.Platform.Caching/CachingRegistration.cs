using Frappe.Platform.Health;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Frappe.Platform.Caching;

public static class CachingRegistration
{
    /// <summary>HybridCache: in-memory L1 with Valkey as the shared L2.</summary>
    public static IServiceCollection AddCaching(this IServiceCollection services, IConfiguration configuration)
    {
        var options = ConfigurationOptions.Parse(configuration.GetConnectionString("valkey") ?? "localhost:6379");
        options.AbortOnConnectFail = false;

        services.AddSingleton(_ => new ValkeyConnection(options));
        services.AddStackExchangeRedisCache(_ => { });
        services.AddOptions<RedisCacheOptions>().Configure<ValkeyConnection>((cache, valkey) =>
        {
            cache.InstanceName = "frappe:";
            cache.ConnectionMultiplexerFactory = valkey.GetAsync;
        });
        services.AddHybridCache();

        // A cache: when it is down HybridCache keeps serving from memory, so it only degrades.
        services.AddHealthChecks().AddDependency("valkey", DependencyCriticality.NonCritical, async (sp, ct) =>
        {
            var connection = await sp.GetRequiredService<ValkeyConnection>().GetAsync().WaitAsync(ct);
            await connection.GetDatabase().PingAsync().WaitAsync(ct);
        });

        return services;
    }
}
