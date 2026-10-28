using Alba;
using Frappe.Platform;
using Frappe.Platform.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace Frappe.TestUtilities;

/// <summary>
/// The platform host a module's integration tests run: the given modules on one database, with every external
/// Wolverine transport disabled. The environment picks the durability mode: Development runs Solo, any other
/// environment Balanced, as deployed.
/// </summary>
public static class FrappeTestHost
{
    /// <summary>Builds and starts the host.</summary>
    /// <param name="connectionString">The app role's connection string.</param>
    /// <param name="modules">The modules the host runs.</param>
    /// <param name="configure">Adds test services, such as a log provider or a metrics exporter.</param>
    /// <param name="environment">The host environment.</param>
    /// <returns>The started host.</returns>
    public static Task<IAlbaHost> StartAsync(
        string connectionString,
        IEnumerable<IModule> modules,
        Action<WebApplicationBuilder>? configure = null,
        string? environment = null
    )
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environment ?? Environments.Development }
        );
        builder.Configuration["ConnectionStrings:Frappe"] = connectionString;
        builder.AddFrappePlatform();
        builder.AddFrappeModules([.. modules]);
        builder.Services.DisableAllExternalWolverineTransports();
        configure?.Invoke(builder);
        return AlbaHost.For(builder, static app => app.MapFrappeEndpoints());
    }
}
