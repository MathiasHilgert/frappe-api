using Frappe.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace Frappe.Platform.IntegrationTests.Caching;

/// <summary>The services of one replica, as a host registers them with <c>AddFrappeCaching</c>.</summary>
internal static class CachingHost
{
    /// <summary>A fresh instance name per test, so tests sharing one Valkey never see each other's keys.</summary>
    public static string NewInstanceName() => $"test-{Guid.NewGuid():N}";

    /// <summary>A replica caching on <paramref name="connectionString"/> under <paramref name="instanceName"/>; <paramref name="configure"/> registers anything a test replaces first.</summary>
    public static ServiceProvider Build(string connectionString, string instanceName, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure?.Invoke(services);
        services.AddFrappeCaching(connectionString, instanceName);
        return services.BuildServiceProvider();
    }
}
