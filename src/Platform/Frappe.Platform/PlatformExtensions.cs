using Frappe.Platform.Health;
using Frappe.Platform.Hosting;
using Frappe.Platform.Http;
using Frappe.Platform.Messaging;
using Frappe.Platform.Modularity;
using Frappe.Platform.Persistence;
using Frappe.Platform.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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
        builder.AddTelemetry();
        builder.AddHealth();
        builder.AddHttp();
        // Responses, Contracts records and messages carry Kernel value objects; requests stay primitive.
        builder.Services.ConfigureHttpJsonOptions(static options =>
            options.SerializerOptions.AddKernelValueObjectConverters()
        );
        // Before persistence, so every context it and the modules register joins the bus's transaction.
        builder.AddMessaging(static json => json.AddKernelValueObjectConverters());
        builder.AddPersistence();
        return builder;
    }

    /// <summary>
    /// Adds the business modules the host runs, after <see cref="AddFrappePlatform{TBuilder}"/>: checks
    /// <paramref name="modules"/> against each other, then calls each one's <see cref="IModule.AddServices"/> in
    /// order and keeps them for <see cref="MapFrappeEndpoints"/>. The checks need no service, so a bad module fails
    /// here, before <c>Build()</c>.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="modules">Every module the host runs, listed explicitly; no assembly is scanned.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// The modules were already added, or they break a boot check; the message lists every violation.
    /// </exception>
    public static TBuilder AddFrappeModules<TBuilder>(
        this TBuilder builder,
        params IModule[] modules
    )
        where TBuilder : IHostApplicationBuilder => builder.AddModules(modules);

    /// <summary>
    /// Adds the HTTP middleware, then maps the platform endpoints. Health is mapped in every role so every replica
    /// can be probed; module endpoints only in the roles that serve business endpoints.
    /// </summary>
    public static WebApplication MapFrappeEndpoints(this WebApplication app)
    {
        app.UseHttp();
        app.MapHealthEndpoints();
        app.MapModuleEndpoints();
        return app;
    }
}
