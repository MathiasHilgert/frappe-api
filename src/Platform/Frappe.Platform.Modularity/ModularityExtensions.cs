using Frappe.Platform.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.Modularity;

/// <summary>Composes the business modules: boot checks and services before the build, route groups after it.</summary>
public static class ModularityExtensions
{
    /// <summary>
    /// Maps one route group per module, <c>/v1/{RoutePrefix}</c> tagged with the module name, and lets the module map
    /// its endpoints inside it. Only the <see cref="HostRole.Api"/> and <see cref="HostRole.All"/> roles serve
    /// business endpoints; the other roles map none.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapModuleEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        HostRole role = app.Services.GetRequiredService<IOptions<PlatformOptions>>().Value.Role;
        if (role is not (HostRole.Api or HostRole.All))
        {
            return app;
        }

        RouteGroupBuilder v1 = app.MapGroup("/v1");
        foreach (IModule module in app.Services.GetServices<IModule>())
        {
            module.MapEndpoints(
                v1.MapGroup($"/{module.Metadata.RoutePrefix}").WithTags(module.Metadata.Name)
            );
        }

        return app;
    }

    // AddFrappeModules is the one public entry point; it documents the boot checks.
    internal static TBuilder AddModules<TBuilder>(this TBuilder builder, params IModule[] modules)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);
        if (Array.Exists(modules, static module => module is null))
        {
            throw new ArgumentException("No module may be null.", nameof(modules));
        }

        // A second list would register a module's services twice, so the one list is the whole composition.
        if (builder.Properties.ContainsKey(typeof(IModule)))
        {
            throw new InvalidOperationException(
                "The modules are already added; pass every module in one AddFrappeModules call."
            );
        }

        // Marked only once the checks pass, so a retry after a failed check reports the violations again.
        ModuleChecks.ThrowIfInvalid(modules);
        builder.Properties[typeof(IModule)] = true;
        builder.AddHostRole();
        foreach (IModule module in modules)
        {
            // TODO: the diner PR skips a module its enable flag turns off, here and in MapModuleEndpoints; the boot
            // checks then run on the enabled set only.
            module.AddServices(builder);
            builder.Services.AddSingleton(module);
        }

        // TODO: the Wolverine bus PR includes each module's assembly in handler discovery (IncludeAssembly) here,
        // with MultipleHandlerBehavior.Separated so two modules handling one event each get their own handler.
        return builder;
    }
}
