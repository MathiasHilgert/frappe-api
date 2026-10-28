using Frappe.Platform.Health;
using Frappe.Platform.Hosting;
using Frappe.Platform.Modularity;
using Frappe.Platform.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform;

/// <summary>
/// The platform defaults every Frappé process shares. <c>Program.cs</c> calls
/// <see cref="AddFrappePlatform{TBuilder}"/> before building and <see cref="MapFrappeEndpoints"/> after.
/// </summary>
public static class PlatformExtensions
{
    /// <summary>
    /// Registers the platform services; <c>Platform:Role</c> and <c>ConnectionStrings:Frappe</c> are
    /// validated when the host starts.
    /// </summary>
    public static TBuilder AddFrappePlatform<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.AddHostRole();
        builder.AddHealth();
        builder.AddPersistence();
        return builder;
    }

    /// <summary>
    /// Adds the business modules the host runs, after <see cref="AddFrappePlatform{TBuilder}"/>; see
    /// <see cref="ModularityExtensions.AddModules{TBuilder}"/> for the boot checks.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="modules">Every module the host runs, listed explicitly.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder AddFrappeModules<TBuilder>(
        this TBuilder builder,
        params IModule[] modules
    )
        where TBuilder : IHostApplicationBuilder => builder.AddModules(modules);

    /// <summary>
    /// Maps the platform endpoints. Health is mapped in every role so every replica can be probed; module endpoints
    /// only in the roles that serve business endpoints.
    /// </summary>
    public static WebApplication MapFrappeEndpoints(this WebApplication app)
    {
        app.MapHealthEndpoints();
        app.MapModuleEndpoints();
        return app;
    }
}
