using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Frappe.Platform.Health;

/// <summary>
/// Health checks for Frappé processes: the liveness check, dependency registration by <see cref="Criticality"/>,
/// and the Kubernetes probe endpoints.
/// </summary>
/// <example>
/// An adapter registers its dependency next to its other services:
/// <code>
/// builder.Services.AddHealthChecks()
///     .AddDependency&lt;PostgresHealthCheck&gt;("postgres", Criticality.Critical);
/// </code>
/// </example>
public static class HealthExtensions
{
    /// <summary>The timeout a dependency check gets when <c>AddDependency</c> receives none.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Registers the health check services and the trivial <c>self</c> check tagged <see cref="HealthTags.Live"/>,
    /// so liveness proves only that the process answers.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder AddHealth<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder
            .Services.AddHealthChecks()
            .AddCheck("self", static () => HealthCheckResult.Healthy(), [HealthTags.Live]);
        return builder;
    }

    /// <summary>
    /// Registers the health check of an external dependency, built from the container like
    /// <see cref="HealthChecksBuilderAddCheckExtensions.AddCheck{T}(IHealthChecksBuilder, string, HealthStatus?, IEnumerable{string})"/>.
    /// </summary>
    /// <typeparam name="TCheck">The check; resolved from the container, or created with its constructor dependencies injected.</typeparam>
    /// <param name="builder">The health checks builder returned by <c>AddHealthChecks()</c>.</param>
    /// <param name="name">The check name shown in the detailed report, for example <c>"postgres"</c>.</param>
    /// <param name="criticality">
    /// <see cref="Criticality.Critical"/> fails with <c>Unhealthy</c> and the <see cref="HealthTags.Critical"/> tag;
    /// <see cref="Criticality.NonCritical"/> fails with <c>Degraded</c> and the <see cref="HealthTags.NonCritical"/> tag.
    /// </param>
    /// <param name="timeout">How long the check may run before it reports its failure status; <see cref="DefaultTimeout"/> when null.</param>
    /// <param name="tags">Extra tags, for example <see cref="HealthTags.Startup"/>; never <see cref="HealthTags.Live"/>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// A check reports a failure as <c>new HealthCheckResult(context.Registration.FailureStatus)</c>, as community
    /// checks do, or throws; a thrown exception and an exceeded timeout also report the failure status.
    /// </remarks>
    public static IHealthChecksBuilder AddDependency<TCheck>(
        this IHealthChecksBuilder builder,
        string name,
        Criticality criticality,
        TimeSpan? timeout = null,
        params string[] tags
    )
        where TCheck : class, IHealthCheck =>
        builder.AddDependency(
            name,
            criticality,
            static services => ActivatorUtilities.GetServiceOrCreateInstance<TCheck>(services),
            timeout,
            tags
        );

    /// <summary>Registers the health check of an external dependency, built by <paramref name="factory"/>.</summary>
    /// <param name="builder">The health checks builder returned by <c>AddHealthChecks()</c>.</param>
    /// <param name="name">The check name shown in the detailed report, for example <c>"postgres"</c>.</param>
    /// <param name="criticality">
    /// <see cref="Criticality.Critical"/> fails with <c>Unhealthy</c> and the <see cref="HealthTags.Critical"/> tag;
    /// <see cref="Criticality.NonCritical"/> fails with <c>Degraded</c> and the <see cref="HealthTags.NonCritical"/> tag.
    /// </param>
    /// <param name="factory">Builds the check each time it runs, from the request's service provider.</param>
    /// <param name="timeout">How long the check may run before it reports its failure status; <see cref="DefaultTimeout"/> when null.</param>
    /// <param name="tags">Extra tags, for example <see cref="HealthTags.Startup"/>; never <see cref="HealthTags.Live"/>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="criticality"/> is not a defined value.</exception>
    /// <remarks>
    /// A check reports a failure as <c>new HealthCheckResult(context.Registration.FailureStatus)</c>, as community
    /// checks do, or throws; a thrown exception and an exceeded timeout also report the failure status.
    /// </remarks>
    public static IHealthChecksBuilder AddDependency(
        this IHealthChecksBuilder builder,
        string name,
        Criticality criticality,
        Func<IServiceProvider, IHealthCheck> factory,
        TimeSpan? timeout = null,
        params string[] tags
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(tags);
        (HealthStatus failureStatus, string criticalityTag) = criticality switch
        {
            Criticality.Critical => (HealthStatus.Unhealthy, HealthTags.Critical),
            Criticality.NonCritical => (HealthStatus.Degraded, HealthTags.NonCritical),
            _ => throw new ArgumentOutOfRangeException(
                nameof(criticality),
                criticality,
                message: null
            ),
        };
        return builder.Add(
            new HealthCheckRegistration(
                name,
                factory,
                failureStatus,
                [criticalityTag, .. tags],
                timeout ?? DefaultTimeout
            )
        );
    }

    /// <summary>
    /// Maps the probe endpoints, each answering the plain-text status with 200 for <c>Healthy</c> and <c>Degraded</c>
    /// and 503 for <c>Unhealthy</c>:
    /// <list type="bullet">
    /// <item><c>/health/live</c> runs only <see cref="HealthTags.Live"/>, never a dependency, so a failing dependency cannot restart the process.</item>
    /// <item><c>/health/startup</c> runs <see cref="HealthTags.Startup"/> and critical dependencies.</item>
    /// <item><c>/health/ready</c> runs only critical dependencies.</item>
    /// <item><c>/health</c> runs every check and answers JSON with each status, duration and description, never exception text or data; mapped only in Development.</item>
    /// </list>
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapHealthChecks("/health/live", Selecting(HealthTags.Live));
        app.MapHealthChecks("/health/startup", Selecting(HealthTags.Startup, HealthTags.Critical));
        app.MapHealthChecks("/health/ready", Selecting(HealthTags.Critical));
        // The detailed report names every dependency, so it stays off deployed environments until it can require authentication.
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(
                "/health",
                new HealthCheckOptions { ResponseWriter = WriteReportAsync }
            );
        }

        return app;
    }

    private static HealthCheckOptions Selecting(params string[] tags) =>
        new() { Predicate = check => tags.Any(check.Tags.Contains) };

    private static async Task WriteReportAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        await using Utf8JsonWriter json = new(context.Response.Body);
        json.WriteStartObject();
        json.WriteString("status", report.Status.ToString());
        json.WriteString(
            "duration",
            report.TotalDuration.ToString("c", CultureInfo.InvariantCulture)
        );
        json.WriteStartObject("entries");
        foreach ((string name, HealthReportEntry entry) in report.Entries)
        {
            json.WriteStartObject(name);
            json.WriteString("status", entry.Status.ToString());
            json.WriteString(
                "duration",
                entry.Duration.ToString("c", CultureInfo.InvariantCulture)
            );
            // A failed check's description is its exception message, which can carry hosts or credentials.
            if (entry.Exception is null && entry.Description is not null)
            {
                json.WriteString("description", entry.Description);
            }

            json.WriteEndObject();
        }

        json.WriteEndObject();
        json.WriteEndObject();
        await json.FlushAsync(context.RequestAborted);
    }
}
