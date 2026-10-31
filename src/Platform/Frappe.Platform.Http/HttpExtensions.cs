using Frappe.Platform.Http.Localization;
using Frappe.Platform.Http.OpenApi;
using Frappe.Platform.Http.Problems;
using Frappe.Platform.Http.RateLimiting;
using Frappe.Platform.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.Http;

/// <summary>
/// The HTTP surface every endpoint shares: JSON options, Problem Details for every failure, request timeouts, rate
/// limits, cursor paging and the OpenAPI document.
/// </summary>
public static class HttpExtensions
{
    /// <summary>Registers the HTTP services; <c>RateLimiting</c> is validated when the host starts.</summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder AddHttp<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        IServiceCollection services = builder.Services;
        services.ConfigureHttpJsonOptions(JsonOptions.Configure);
        services.AddProblemDetails(static options =>
            options.CustomizeProblemDetails = ProblemMapper.Customize
        );
        // Without it, a body or parameter that fails to bind is an empty 400 outside Development.
        services.Configure<RouteHandlerOptions>(static options => options.ThrowOnBadRequest = true);
        services.AddExceptionHandler<ExceptionHandler>();
        services.AddRequestTimeouts(RequestTimeouts.Configure);
        RateLimitPolicies.Add(services);
        services.AddSingleton(static provider => new ErrorTexts([
            typeof(HttpExtensions).Assembly,
            .. provider.GetServices<IModule>().SelectMany(ModuleTypes.Assemblies),
        ]));
        // Loads the texts at start, so a broken resource fails the boot instead of the first error response.
        services.AddHostedService<ErrorTextsCheck>();
        // TODO: set FallbackPolicy to require an authenticated user when authentication arrives (anonymous
        // endpoints already opt out with AllowAnonymous)
        OpenApiExtensions.AddFrappeOpenApi(services);
        return builder;
    }

    /// <summary>
    /// Adds the HTTP middleware in order (exception handler, status code problems, request timeouts, rate limiter) and maps the OpenAPI
    /// document and Scalar in Development; call it before mapping any endpoint.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication UseHttp(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseExceptionHandler();
        // A bodiless error status (routing, body reading, rate limiter, timeout) becomes the house problem.
        app.UseStatusCodePages();
        app.UseRequestTimeouts();
        app.UseRateLimiter();
        OpenApiExtensions.MapFrappeOpenApi(app);
        return app;
    }
}
