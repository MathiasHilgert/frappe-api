using Frappe.Modularity;
using Frappe.Platform.Caching;
using Frappe.Platform.Health;
using Frappe.Platform.Localization;
using Frappe.Platform.Messaging;
using Frappe.Platform.Persistence;
using Frappe.Platform.Realtime;
using Frappe.Platform.Web.Correlation;
using Frappe.Platform.Web.Forwarding;
using Frappe.Platform.Web.Security;

namespace Frappe.Api;

/// <summary>Composition root: the only place that knows every platform adapter and every module.</summary>
internal static class PlatformComposition
{
    public static WebApplicationBuilder AddFrappe(this WebApplicationBuilder builder, IReadOnlyList<IModule> modules)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        // Fail at startup, in every environment, on missing registrations or captive dependencies.
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });

        builder.AddObservability(modules);

        services.AddPlatformLocalization();
        services.AddTrustedForwardedHeaders(configuration);
        services.AddRequestCorrelation();
        services.AddDenyByDefaultSecurity();
        services.AddAnonymousRateLimiting();
        services.AddSingleton(TimeProvider.System);
        services.AddPlatformHealth();
        services.AddPostgres(configuration);
        services.AddRealtime(configuration);
        services.AddCaching(configuration, builder.Environment);
        services.AddJobs(configuration.GetPostgresConnectionString(), builder.Environment);
        services.AddOpenApiDocs();

        foreach (var module in modules)
            module.AddServices(services, configuration);

        builder.AddMessaging(
            configuration.GetPostgresConnectionString(),
            modules,
            typeof(PlatformComposition).Assembly,
            typeof(RealtimePushHandler).Assembly);

        return builder;
    }

    /// <summary>
    /// The request pipeline. Order matters: forwarded headers run first so everything sees the real client address
    /// (only from trusted proxies); correlation runs next so every response and error carries the
    /// request id; error handling wraps everything after it; localization follows authentication so a stored
    /// preference can be read, and precedes the rate limiter and authorization so 429 and 401/403 problems are translated too.
    /// </summary>
    public static WebApplication UseFrappe(this WebApplication app)
    {
        app.UseTrustedForwardedHeaders();
        app.UseRequestCorrelation();
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UsePlatformLocalization();
        app.UseRateLimiter();
        app.UseAuthorization();

        return app;
    }

    public static WebApplication MapFrappe(this WebApplication app, IReadOnlyList<IModule> modules)
    {
        app.MapPlatformHealth();
        app.MapRealtime();
        app.MapOpenApiDocs();

        foreach (var module in modules)
            module.MapEndpoints(app.MapGroup($"/v1/{module.Name}").WithTags(module.Name));

        return app;
    }
}
