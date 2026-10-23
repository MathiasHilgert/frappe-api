using Frappe.Platform.Health;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Frappe.Platform.Caching;

public static class CachingRegistration
{
    private const string ConnectionStringName = "valkey";

    /// <summary>Local development cache, used only in Development when no "valkey" connection string is set.</summary>
    private const string DevelopmentConnectionString = "localhost:6379";

    /// <summary>
    /// HybridCache: in-memory L1 with Valkey as the shared L2. Outside Development a missing connection string
    /// fails startup, so a deployment never silently points at localhost.
    /// </summary>
    public static IServiceCollection AddCaching(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? (environment.IsDevelopment()
                ? DevelopmentConnectionString
                : throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is missing."));
        var options = ConfigurationOptions.Parse(connectionString);
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
