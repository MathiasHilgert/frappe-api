using Frappe.Modularity;
using Frappe.Platform.Caching;
using Frappe.Platform.Health;
using Frappe.Platform.Messaging;
using Frappe.Platform.Persistence;

namespace Frappe.Api;

/// <summary>Composition root: the only place that knows every platform adapter and every module.</summary>
internal static class PlatformComposition
{
    public static WebApplicationBuilder AddFrappePlatform(this WebApplicationBuilder builder, IReadOnlyList<IModule> modules)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services.AddProblemDetails();
        services.AddSingleton(TimeProvider.System);
        services.AddPlatformHealth();
        services.AddPostgres(configuration);
        services.AddCaching(configuration);
        services.AddJobs();

        foreach (var module in modules)
            module.AddServices(services, configuration);

        builder.AddMessaging(configuration.GetPostgresConnectionString(), modules);

        return builder;
    }

    public static WebApplication MapFrappePlatform(this WebApplication app, IReadOnlyList<IModule> modules)
    {
        app.MapPlatformHealth();

        foreach (var module in modules)
            module.MapEndpoints(app.MapGroup($"/v1/{module.Name}").WithTags(module.Name));

        return app;
    }
}
