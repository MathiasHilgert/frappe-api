using Frappe.Platform.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Frappe.Platform.Modularity;

/// <summary>Composes the business modules: boot checks and services before the build, route groups after it.</summary>
public static class ModularityExtensions
{
    /// <summary>
    /// Checks <paramref name="modules"/> against each other, then calls each one's <see cref="IModule.AddServices"/>
    /// in order and keeps them for <see cref="MapModuleEndpoints"/>. The checks need no service, so a bad module
    /// fails here, before <c>Build()</c>.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="modules">Every module the host runs, listed explicitly; no assembly is scanned.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The modules were already added, or they break a boot check; the message lists every violation.
    /// </exception>
    public static TBuilder AddModules<TBuilder>(this TBuilder builder, params IModule[] modules)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(modules);
        if (Array.Exists(modules, static module => module is null))
        {
            throw new ArgumentException("No module may be null.", nameof(modules));
        }

        // A second list would register a module's services twice, so the one list is the whole composition.
        if (!builder.Properties.TryAdd(typeof(IModule), modules))
        {
            throw new InvalidOperationException(
                "The modules are already added; pass every module in one AddFrappeModules call."
            );
        }

        ModuleChecks.ThrowIfInvalid(modules);
        builder.AddHostRole();
        foreach (IModule module in modules)
        {
            // TODO: the diner PR skips a module its enable flag turns off, here and in MapModuleEndpoints.
            module.AddServices(builder);
            builder.Services.AddSingleton(module);
        }

        // TODO: the Wolverine bus PR includes each module's assembly in handler discovery (IncludeAssembly) here.
        return builder;
    }

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

        foreach (IModule module in app.Services.GetServices<IModule>())
        {
            module.MapEndpoints(
                app.MapGroup($"/v1/{module.Metadata.RoutePrefix}").WithTags(module.Metadata.Name)
            );
        }

        return app;
    }
}
