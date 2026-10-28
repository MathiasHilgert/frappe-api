using Alba;
using Frappe.Platform;
using Frappe.Platform.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace Frappe.TestUtilities;

/// <summary>
/// The platform host a module's integration tests run: the given modules on one database and, when a
/// <see cref="NatsScope"/> is given, on NATS; without one every external Wolverine transport is disabled, so
/// integration events go nowhere. The environment picks the durability mode and the NATS provisioning:
/// Development runs Solo and creates the topology, any other environment Balanced and verifies it, as deployed.
/// </summary>
public static class FrappeTestHost
{
    /// <summary>Builds and starts the host.</summary>
    /// <param name="connectionString">The app role's connection string.</param>
    /// <param name="modules">The modules the host runs.</param>
    /// <param name="configure">Adds test services, such as a log provider or a metrics exporter.</param>
    /// <param name="environment">The host environment.</param>
    /// <param name="nats">The NATS scope, or <see langword="null"/> to disable every external transport.</param>
    /// <returns>The started host.</returns>
    public static Task<IAlbaHost> StartAsync(
        string connectionString,
        IEnumerable<IModule> modules,
        Action<WebApplicationBuilder>? configure = null,
        string? environment = null,
        NatsScope? nats = null
    )
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environment ?? Environments.Development }
        );
        builder.Configuration["ConnectionStrings:Frappe"] = connectionString;
        builder.Configuration.AddInMemoryCollection(
            nats?.Settings ?? [new("Nats:Url", "nats://127.0.0.1:4222")]
        );
        builder.AddFrappePlatform();
        builder.AddFrappeModules([.. modules]);
        if (nats is null)
        {
            builder.Services.DisableAllExternalWolverineTransports();
        }

        configure?.Invoke(builder);
        return AlbaHost.For(builder, static app => app.MapFrappeEndpoints());
    }
}
